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

    internal static (Dictionary<string, string> Tags, IReadOnlyList<string> SanMoves, bool HasContent)
        ParsePgn(string pgn)
    {
        Dictionary<string, string> tags = new(StringComparer.Ordinal);
        foreach (System.Text.RegularExpressions.Match m in TagPattern.Matches(pgn))
        {
            tags[m.Groups[1].Value] = m.Groups[2].Value;
        }

        int lastBracket = pgn.LastIndexOf(']');
        string movetext = lastBracket >= 0 ? pgn[(lastBracket + 1)..] : pgn;

        movetext = CommentPattern.Replace(movetext, " ");
        movetext = AnnotationPattern.Replace(movetext, " ");
        movetext = MoveNumberPattern.Replace(movetext, " ");
        movetext = ResultPattern.Replace(movetext, " ");

        List<string> sanMoves = [.. movetext.Split(
            [' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)];

        bool hasContent = tags.Count > 0 || sanMoves.Count > 0;
        return (tags, sanMoves, hasContent);
    }

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
            CreatedAt: DateTimeOffset.UtcNow);

        return await repo.InsertAsync(game, ct);
    }

    internal async Task<(IReadOnlyList<UserMatchSummary> Matches, long Total, int Page, int PageSize)>
        ListUserMatchesAsync(string userId, int page, int pageSize, CancellationToken ct)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        page = Math.Max(1, page);

        ListResponse[] both = await Task.WhenAll(
            ListMatchesByFieldAsync("white_user_id", userId, ct),
            ListMatchesByFieldAsync("black_user_id", userId, ct));

        Dictionary<string, Struct> deduped = new(StringComparer.Ordinal);
        foreach (Struct record in both[0].Records.Concat(both[1].Records))
        {
            string? id = GetStringField(record, "id");
            if (id is null || deduped.ContainsKey(id))
            {
                continue;
            }

            string status = GetStringField(record, "status") ?? string.Empty;
            if (status == "ongoing" || string.IsNullOrEmpty(status))
            {
                continue;
            }

            deduped[id] = record;
        }

        UserMatchSummary[] resolved = await Task.WhenAll(
            deduped.Values.Select(r => BuildUserMatchSummaryAsync(r, ct)));
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
        string status = GetStringField(match, "status") ?? string.Empty;

        if (status == "ongoing")
        {
            throw new MatchStillOngoingException();
        }

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

        Dictionary<string, string> matchTags = BuildMatchTags(result);
        string pgn = BuildMatchPgn(matchTags, whiteInfo, blackInfo, sanMoves, result);

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
            CreatedAt: DateTimeOffset.UtcNow);

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
            CreatedAt: DateTimeOffset.UtcNow);

        return await repo.InsertAsync(game, ct);
    }

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

    private static Dictionary<string, string> BuildPlayerInfo(string? userId, string? botId) =>
        userId is not null && userId.Length > 0
            ? new Dictionary<string, string> { ["user_id"] = userId }
            : botId is not null && botId.Length > 0
                ? new Dictionary<string, string> { ["bot_id"] = botId }
                : [];

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
        string result)
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
        Struct record,
        CancellationToken ct)
    {
        string? id = GetStringField(record, "id");
        string status = GetStringField(record, "status") ?? string.Empty;
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
            MatchId: id ?? string.Empty,
            White: white,
            Black: black,
            Status: status,
            TimeFormat: tf,
            MoveCount: moves.Count,
            FinishedAtMs: finishedAtMs);
    }
}
