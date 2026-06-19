using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Maichess.Database.V1;
using Maichess.Engine.V1;
using Maichess.MoveValidator.V1;
using Maichess.User.V1;
using MaichessAnalysisService.Domain;

namespace MaichessAnalysisService.Services;

internal sealed class AnalysisGameService(
    IAnalysisGameRepository repo,
    Database.DatabaseClient db,
    Moves.MovesClient movesClient,
    Users.UsersClient usersClient,
    Bots.BotsClient botsClient)
{
    private const string InitialFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    private static readonly Regex TagPattern =
        new(@"\[(\w+)\s+""([^""]*)""\]", RegexOptions.Compiled, TimeSpan.FromSeconds(5));

    private static readonly Regex CommentPattern =
        new(@"\{[^}]*\}", RegexOptions.Compiled, TimeSpan.FromSeconds(5));

    private static readonly Regex AnnotationPattern =
        new(@"\$\d+", RegexOptions.Compiled, TimeSpan.FromSeconds(5));

    private static readonly Regex MoveNumberPattern =
        new(@"\d+\.+", RegexOptions.Compiled, TimeSpan.FromSeconds(5));

    private static readonly Regex ResultPattern =
        new(@"1-0|0-1|1/2-1/2|\*", RegexOptions.Compiled, TimeSpan.FromSeconds(5));

    // Extracts the time token from a PGN clock comment: {[%clk 0:02:59]} or
    // {[%emt 0:00:03]}. Both are treated as the mover's remaining clock — %emt is an
    // elapsed-time annotation, but without a reliable base time we surface the value
    // as-is so imported games still show per-move times.
    private static readonly Regex ClockCommentPattern =
        new(@"\[%(?:clk|emt)\s+([^\]\s]+)\]", RegexOptions.Compiled, TimeSpan.FromSeconds(5));

    // A leading move-number prefix on a single movetext token: "1." or "12...".
    private static readonly Regex MoveNumberPrefixPattern =
        new(@"^\d+\.+", RegexOptions.Compiled, TimeSpan.FromSeconds(5));

    internal static (Dictionary<string, string> Tags, IReadOnlyList<string> SanMoves, bool HasContent)
        ParsePgn(string pgn)
    {
        Dictionary<string, string> tags = new(StringComparer.Ordinal);
        foreach (System.Text.RegularExpressions.Match m in TagPattern.Matches(pgn))
        {
            tags[m.Groups[1].Value] = m.Groups[2].Value;
        }

        string movetext = ExtractMovetext(pgn);

        movetext = CommentPattern.Replace(movetext, " ");
        movetext = AnnotationPattern.Replace(movetext, " ");
        movetext = MoveNumberPattern.Replace(movetext, " ");
        movetext = ResultPattern.Replace(movetext, " ");

        List<string> sanMoves = [.. movetext.Split(
            [' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)];

        bool hasContent = tags.Count > 0 || sanMoves.Count > 0;
        return (tags, sanMoves, hasContent);
    }

    // The movetext is everything after the tag-pair section. Locate it by the end of
    // the last `[Key "Value"]` tag rather than the last ']' — clock comments
    // ({[%clk 0:02:59]}) contain ']', so LastIndexOf(']') would truncate the moves.
    internal static string ExtractMovetext(string pgn)
    {
        System.Text.RegularExpressions.MatchCollection tags = TagPattern.Matches(pgn);
        if (tags.Count == 0)
        {
            return pgn;
        }

        System.Text.RegularExpressions.Match last = tags[^1];
        return pgn[(last.Index + last.Length)..];
    }

    // Parses per-ply remaining clocks from an imported PGN's {[%clk ...]}/{[%emt ...]}
    // comments, aligned to the SAN moves ParsePgn extracts. clocks[i] is the mover's
    // remaining time at ply i, or null when that move carried no clock comment.
    internal static IReadOnlyList<long?> ParseMoveClocks(string pgn)
    {
        string movetext = ExtractMovetext(pgn);

        // Strip annotations ($N) and the game result but keep {comments} and move
        // numbers so each clock comment stays attached to the move it follows.
        movetext = AnnotationPattern.Replace(movetext, " ");
        movetext = ResultPattern.Replace(movetext, " ");

        List<long?> clocks = [];
        int i = 0;
        while (i < movetext.Length)
        {
            char c = movetext[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            if (c == '{')
            {
                int end = movetext.IndexOf('}', i);
                if (end < 0)
                {
                    break;
                }

                long? ms = ParseClockComment(movetext[i..(end + 1)]);
                if (ms is not null && clocks.Count > 0 && clocks[^1] is null)
                {
                    clocks[^1] = ms;
                }

                i = end + 1;
                continue;
            }

            int start = i;
            while (i < movetext.Length && !char.IsWhiteSpace(movetext[i]) && movetext[i] != '{')
            {
                i++;
            }

            string token = MoveNumberPrefixPattern.Replace(movetext[start..i], string.Empty);
            if (token.Length > 0)
            {
                clocks.Add(null);
            }
        }

        return clocks;
    }

    // Builds a ClockHistory parallel to the moves from per-ply remaining clocks: the
    // mover's slot takes the parsed value, the opposite side carries its previous
    // value forward. Returns empty when no ply carried a clock (treated as no data).
    internal static IReadOnlyList<ClockSnapshot> BuildClockHistory(int moveCount, IReadOnlyList<long?> plyClocks)
    {
        bool any = false;
        for (int i = 0; i < moveCount && i < plyClocks.Count; i++)
        {
            if (plyClocks[i] is not null)
            {
                any = true;
                break;
            }
        }

        if (!any)
        {
            return [];
        }

        List<ClockSnapshot> history = new(moveCount);
        long white = 0;
        long black = 0;
        for (int i = 0; i < moveCount; i++)
        {
            long? clk = i < plyClocks.Count ? plyClocks[i] : null;
            if (i % 2 == 0)
            {
                if (clk is not null)
                {
                    white = clk.Value;
                }
            }
            else if (clk is not null)
            {
                black = clk.Value;
            }

            history.Add(new ClockSnapshot(white, black));
        }

        return history;
    }

    // Parses an "H:MM:SS", "MM:SS" or "SS" clock token (fractional seconds allowed)
    // into milliseconds. Returns null for an unparseable or negative token.
    internal static long? ParseClockMs(string token)
    {
        string[] parts = token.Split(':');
        if (parts.Length is 0 or > 3)
        {
            return null;
        }

        double seconds = 0;
        foreach (string part in parts)
        {
            if (!double.TryParse(part, NumberStyles.Number, CultureInfo.InvariantCulture, out double value) ||
                value < 0)
            {
                return null;
            }

            seconds = (seconds * 60) + value;
        }

        return (long)(seconds * 1000);
    }

    // Renders remaining milliseconds as the PGN-standard "H:MM:SS" clock annotation.
    internal static string FormatPgnClock(long ms)
    {
        long totalSeconds = Math.Max(0, ms) / 1000;
        long hours = totalSeconds / 3600;
        long minutes = (totalSeconds % 3600) / 60;
        long seconds = totalSeconds % 60;
        return string.Create(CultureInfo.InvariantCulture, $"{hours}:{minutes:D2}:{seconds:D2}");
    }

    internal static UserMatchStatusFilter ParseStatusFilter(string? status) => status switch
    {
        null or "" or "finished" => UserMatchStatusFilter.Finished,
        "ongoing" => UserMatchStatusFilter.Ongoing,
        "all" => UserMatchStatusFilter.All,
        _ => throw new InvalidMatchStatusFilterException(status),
    };

    internal async Task<AnalysisGame> GetGameAsync(string id, string userId, CancellationToken ct)
    {
        AnalysisGame game = await repo.GetByIdAsync(id, ct) ?? throw new AnalysisGameNotFoundException();
        return game.UserId != userId ? throw new AccessDeniedException() : game;
    }

    internal async Task<(IReadOnlyList<AnalysisGame> Games, long Total, int Page, int PageSize)> ListGamesAsync(
        string userId, int page, int pageSize, CancellationToken ct)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        page = Math.Max(1, page);
        int offset = (page - 1) * pageSize;

        Task<long> countTask = repo.CountByUserIdAsync(userId, ct);
        Task<IReadOnlyList<AnalysisGame>> gamesTask = repo.ListByUserIdAsync(userId, pageSize, offset, ct);

        await Task.WhenAll(countTask, gamesTask);

        return (await gamesTask, await countTask, page, pageSize);
    }

    internal async Task DeleteGameAsync(string id, string userId, CancellationToken ct)
    {
        AnalysisGame game = await repo.GetByIdAsync(id, ct) ?? throw new AnalysisGameNotFoundException();
        if (game.UserId != userId)
        {
            throw new AccessDeniedException();
        }

        await repo.DeleteAsync(id, ct);
    }

    internal async Task<AnalysisGame> ImportFromPgnAsync(string pgn, string userId, CancellationToken ct)
    {
        (Dictionary<string, string> tags, IReadOnlyList<string> sanMoves, bool hasContent) = ParsePgn(pgn);
        if (!hasContent)
        {
            throw new InvalidPgnException("empty pgn");
        }

        string startingFen = tags.TryGetValue("FEN", out string? fenTag) ? fenTag : InitialFen;
        string currentFen = startingFen;
        List<string> positionHistory = [];
        List<string> uciMoves = [];
        List<string> fens = [];

        foreach (string san in sanMoves)
        {
            ValidateMoveSanRequest req = new() { Fen = currentFen, Move = san };
            req.PositionHistory.AddRange(positionHistory);
            ValidateMoveSanResponse resp = await movesClient.ValidateMoveSanAsync(req, cancellationToken: ct);

            if (!resp.Valid)
            {
                throw new InvalidPgnException(resp.Reason);
            }

            uciMoves.Add(resp.UciMove);
            fens.Add(resp.ResultingFen);
            currentFen = resp.ResultingFen;
            positionHistory = [.. resp.PositionHistory];
        }

        IReadOnlyList<ClockSnapshot> clockHistory = BuildClockHistory(uciMoves.Count, ParseMoveClocks(pgn));

        AnalysisGame game = new(
            Id: string.Empty,
            UserId: userId,
            Source: "pgn",
            MatchId: null,
            StartingFen: startingFen,
            Moves: uciMoves,
            Fens: fens,
            Pgn: pgn.Trim(),
            Result: tags.GetValueOrDefault("Result", "*"),
            White: new Dictionary<string, string> { ["name"] = tags.GetValueOrDefault("White", "?") },
            Black: new Dictionary<string, string> { ["name"] = tags.GetValueOrDefault("Black", "?") },
            Tags: tags,
            CreatedAt: DateTimeOffset.UtcNow,
            ClockHistory: clockHistory);

        return await repo.InsertAsync(game, ct);
    }

    internal async Task<(IReadOnlyList<UserMatchSummary> Matches, long Total, int Page, int PageSize)>
        ListUserMatchesAsync(string userId, UserMatchStatusFilter filter, int page, int pageSize, CancellationToken ct)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        page = Math.Max(1, page);

        ListResponse[] both = await Task.WhenAll(
            ListMatchesByFieldAsync("white_user_id", userId, ct),
            ListMatchesByFieldAsync("black_user_id", userId, ct));

        Dictionary<string, (string Status, Struct Record)> deduped = new(StringComparer.Ordinal);
        foreach (Struct record in both[0].Records.Concat(both[1].Records))
        {
            string? id = GetStringField(record, "id");
            if (id is null || deduped.ContainsKey(id))
            {
                continue;
            }

            string status = GetStringField(record, "status") ?? string.Empty;
            if (string.IsNullOrEmpty(status) || !MatchesStatusFilter(status, filter))
            {
                continue;
            }

            deduped[id] = (status, record);
        }

        UserMatchSummary[] resolved = await Task.WhenAll(
            deduped.Select(kv => BuildUserMatchSummaryAsync(kv.Key, kv.Value.Status, kv.Value.Record, ct)));
        List<UserMatchSummary> all = [.. resolved.OrderByDescending(m => m.FinishedAtMs)];

        long total = all.Count;
        int offset = (page - 1) * pageSize;
        IReadOnlyList<UserMatchSummary> pageItems = offset >= all.Count
            ? []
            : all.Skip(offset).Take(pageSize).ToList();

        return (pageItems, total, page, pageSize);
    }

    internal async Task<AnalysisGame> ImportFromMatchAsync(string matchId, string userId, CancellationToken ct)
    {
        GetResponse matchResp;
        try
        {
            matchResp = await db.GetAsync(
                new GetRequest { Collection = "matches", Id = matchId },
                cancellationToken: ct);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            throw new AnalysisGameNotFoundException();
        }

        Struct match = matchResp.Record;

        // An ongoing match is importable too: it yields a snapshot of the moves
        // played so far (result "*"), so a player can review a game in progress.
        string status = GetStringField(match, "status") ?? string.Empty;

        string? whiteUserId = GetStringField(match, "white_user_id");
        string? blackUserId = GetStringField(match, "black_user_id");
        string? whiteBotId = GetStringField(match, "white_bot_id");
        string? blackBotId = GetStringField(match, "black_bot_id");
        string? createdByUserId = GetStringField(match, "created_by_user_id");

        // A user may import a match they played (either colour) or one they
        // started. The latter covers bot-vs-bot games they spawned, which appear
        // in their Past Matches via created_by yet occupy neither colour.
        bool isAuthorized = whiteUserId == userId || blackUserId == userId || createdByUserId == userId;
        if (!isAuthorized)
        {
            throw new MatchAccessDeniedException();
        }

        List<string> moves = GetStringList(match, "moves");
        List<string> fenHistory = GetStringList(match, "fen_history");

        string startingFen = fenHistory.Count > 0 ? fenHistory[0] : InitialFen;
        List<string> fens = fenHistory.Count > 1 ? fenHistory[1..] : [];

        List<string> sanMoves = [];
        if (moves.Count > 0)
        {
            ConvertSequenceToSanRequest sanReq = new() { StartingFen = startingFen };
            sanReq.UciMoves.AddRange(moves);
            ConvertSequenceToSanResponse sanResp = await movesClient.ConvertSequenceToSanAsync(
                sanReq, cancellationToken: ct);
            sanMoves = [.. sanResp.SanMoves];
        }

        Dictionary<string, string> whiteInfo = await ResolvePlayerInfoAsync(whiteUserId, whiteBotId, ct);
        Dictionary<string, string> blackInfo = await ResolvePlayerInfoAsync(blackUserId, blackBotId, ct);

        string result = status switch
        {
            "white_won" => "1-0",
            "black_won" => "0-1",
            "draw" => "1/2-1/2",
            _ => "*",
        };

        IReadOnlyList<ClockSnapshot> clockHistory = ReadClockHistory(match);

        Dictionary<string, string> matchTags = BuildMatchTags(result);
        string pgn = BuildMatchPgn(matchTags, whiteInfo, blackInfo, sanMoves, result, clockHistory);

        AnalysisGame game = new(
            Id: string.Empty,
            UserId: userId,
            Source: "match",
            MatchId: matchId,
            StartingFen: startingFen,
            Moves: moves,
            Fens: fens,
            Pgn: pgn,
            Result: result,
            White: whiteInfo,
            Black: blackInfo,
            Tags: matchTags,
            CreatedAt: DateTimeOffset.UtcNow,
            ClockHistory: clockHistory);

        return await repo.InsertAsync(game, ct);
    }

    internal async Task<AnalysisGame> ImportFromFenAsync(string fen, string userId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(fen))
        {
            throw new InvalidPgnException("fen must not be empty");
        }

        AnalysisGame game = new(
            Id: string.Empty,
            UserId: userId,
            Source: "fen",
            MatchId: null,
            StartingFen: fen,
            Moves: [],
            Fens: [],
            Pgn: $"[FEN \"{fen}\"]\n[SetUp \"1\"]\n\n*",
            Result: "*",
            White: new Dictionary<string, string>(),
            Black: new Dictionary<string, string>(),
            Tags: new Dictionary<string, string> { ["FEN"] = fen, ["SetUp"] = "1" },
            CreatedAt: DateTimeOffset.UtcNow,
            ClockHistory: []);

        return await repo.InsertAsync(game, ct);
    }

    private static bool MatchesStatusFilter(string status, UserMatchStatusFilter filter) => filter switch
    {
        UserMatchStatusFilter.Ongoing => status == "ongoing",
        UserMatchStatusFilter.Finished => status != "ongoing",
        _ => true,
    };

    private static string? GetStringField(Struct s, string key) =>
        s.Fields.TryGetValue(key, out Value? v) && v.KindCase == Value.KindOneofCase.StringValue
            ? v.StringValue
            : null;

    private static List<string> GetStringList(Struct s, string key) =>
        s.Fields.TryGetValue(key, out Value? v) && v.KindCase == Value.KindOneofCase.ListValue
            ? [.. v.ListValue.Values
                .Where(x => x.KindCase == Value.KindOneofCase.StringValue)
                .Select(x => x.StringValue)]
            : [];

    // Reads a match document's clock_history (one {white_time_ms, black_time_ms}
    // sub-struct per applied move). Absent/empty for matches that predate clock
    // history — an empty list is the correct "no clock data" representation.
    private static IReadOnlyList<ClockSnapshot> ReadClockHistory(Struct match) =>
        match.Fields.TryGetValue("clock_history", out Value? v) && v.KindCase == Value.KindOneofCase.ListValue
            ? [.. v.ListValue.Values
                .Where(e => e.KindCase == Value.KindOneofCase.StructValue)
                .Select(e => new ClockSnapshot(
                    (long)(e.StructValue.Fields.TryGetValue("white_time_ms", out Value? w) ? w.NumberValue : 0),
                    (long)(e.StructValue.Fields.TryGetValue("black_time_ms", out Value? b) ? b.NumberValue : 0)))]
            : [];

    private static long? ParseClockComment(string comment)
    {
        System.Text.RegularExpressions.Match m = ClockCommentPattern.Match(comment);
        return m.Success ? ParseClockMs(m.Groups[1].Value) : null;
    }

    private static Dictionary<string, string> BuildMatchTags(string result) =>
        new()
        {
            ["Event"] = "Maichess Match",
            ["Site"] = "maichess",
            ["Date"] = DateTimeOffset.UtcNow.ToString("yyyy.MM.dd", CultureInfo.InvariantCulture),
            ["Result"] = result,
        };

    private static string BuildMatchPgn(
        Dictionary<string, string> tags,
        Dictionary<string, string> white,
        Dictionary<string, string> black,
        List<string> sanMoves,
        string result,
        IReadOnlyList<ClockSnapshot> clockHistory)
    {
        string whiteName = white.TryGetValue("username", out string? wun) ? wun
            : white.TryGetValue("name", out string? wn) ? wn
            : white.TryGetValue("user_id", out string? wid) ? wid
            : white.TryGetValue("bot_id", out string? wbid) ? wbid : "?";
        string blackName = black.TryGetValue("username", out string? bun) ? bun
            : black.TryGetValue("name", out string? bn) ? bn
            : black.TryGetValue("user_id", out string? bid) ? bid
            : black.TryGetValue("bot_id", out string? bbid) ? bbid : "?";

        StringBuilder sb = new();
        foreach ((string key, string val) in tags)
        {
            sb.AppendLine($"[{key} \"{val}\"]");
        }

        sb.AppendLine($"[White \"{whiteName}\"]");
        sb.AppendLine($"[Black \"{blackName}\"]");
        sb.AppendLine();

        for (int i = 0; i < sanMoves.Count; i++)
        {
            if (i % 2 == 0)
            {
                sb.Append(CultureInfo.InvariantCulture, $"{(i / 2) + 1}. ");
            }

            sb.Append(sanMoves[i]);

            // The clock for the side that just moved: white on even plies, black on odd.
            // Absent clock data leaves the movetext exactly as it was before this feature.
            if (i < clockHistory.Count)
            {
                long remaining = i % 2 == 0 ? clockHistory[i].WhiteTimeMs : clockHistory[i].BlackTimeMs;
                sb.Append(CultureInfo.InvariantCulture, $" {{[%clk {FormatPgnClock(remaining)}]}}");
            }

            sb.Append(' ');
        }

        sb.Append(result);
        return sb.ToString().TrimEnd();
    }

    private static UserMatchTimeFormat ReadTimeFormat(Struct record)
    {
        string? id = GetStringField(record, "time_format_id");
        if (id is not null)
        {
            return new UserMatchTimeFormat(
                Id: id,
                BaseMs: (long)(record.Fields.TryGetValue("time_format_base_ms", out Value? b) ? b.NumberValue : 0),
                IncrementMs: (long)(record.Fields.TryGetValue("time_format_increment_ms", out Value? inc) ? inc.NumberValue : 0),
                Category: GetStringField(record, "time_format_category") ?? string.Empty);
        }

        string legacy = GetStringField(record, "time_control") ?? "blitz";
        (string fallbackId, long baseMs) = legacy switch
        {
            "bullet" => ("3+0", 180_000L),
            "blitz" => ("5+0", 300_000L),
            "rapid" => ("10+0", 600_000L),
            "classical" => ("30+0", 1_800_000L),
            _ => ("5+0", 300_000L),
        };
        return new UserMatchTimeFormat(fallbackId, baseMs, 0, legacy);
    }

    private static long ParseLastMoveAtMs(Struct record)
    {
        string? raw = GetStringField(record, "last_move_at");
        return raw is not null && DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, out DateTimeOffset dt)
            ? dt.ToUnixTimeMilliseconds()
            : 0L;
    }

    private Task<ListResponse> ListMatchesByFieldAsync(string field, string userId, CancellationToken ct)
    {
        Struct filter = new();
        filter.Fields[field] = Value.ForString(userId);
        return db.ListAsync(
            new ListRequest { Collection = "matches", Filter = filter },
            cancellationToken: ct).ResponseAsync;
    }

    private async Task<Dictionary<string, string>> ResolvePlayerInfoAsync(
        string? userId,
        string? botId,
        CancellationToken ct)
    {
        if (userId is not null && userId.Length > 0)
        {
            Dictionary<string, string> info = new() { ["user_id"] = userId };
            try
            {
                GetUserResponse userResp = await usersClient.GetUserAsync(
                    new GetUserRequest { UserId = userId },
                    cancellationToken: ct);
                info["username"] = userResp.User.Username;
            }
            catch (RpcException)
            {
            }

            return info;
        }

        if (botId is not null && botId.Length > 0)
        {
            Dictionary<string, string> info = new() { ["bot_id"] = botId };
            try
            {
                ListBotsResponse bots = await botsClient.ListBotsAsync(
                    new ListBotsRequest(),
                    cancellationToken: ct);
                Bot? bot = bots.Bots.FirstOrDefault(b => b.Id == botId);
                if (bot is not null)
                {
                    info["name"] = bot.Name;
                }
            }
            catch (RpcException)
            {
            }

            return info;
        }

        return [];
    }

    private async Task<UserMatchSummary> BuildUserMatchSummaryAsync(
        string id,
        string status,
        Struct record,
        CancellationToken ct)
    {
        Dictionary<string, string> white = await ResolvePlayerInfoAsync(
            GetStringField(record, "white_user_id"),
            GetStringField(record, "white_bot_id"),
            ct);
        Dictionary<string, string> black = await ResolvePlayerInfoAsync(
            GetStringField(record, "black_user_id"),
            GetStringField(record, "black_bot_id"),
            ct);

        List<string> moves = GetStringList(record, "moves");
        UserMatchTimeFormat tf = ReadTimeFormat(record);
        long finishedAtMs = ParseLastMoveAtMs(record);

        return new UserMatchSummary(
            MatchId: id,
            White: white,
            Black: black,
            Status: status,
            TimeFormat: tf,
            MoveCount: moves.Count,
            FinishedAtMs: finishedAtMs);
    }
}
