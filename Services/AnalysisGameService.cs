using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Maichess.MatchManager.V1;
using Maichess.MoveValidator.V1;
using MaichessAnalysisService.Domain;

namespace MaichessAnalysisService.Services;

using ProtoMatch = Maichess.MatchManager.V1.Match;
using RegexMatch = System.Text.RegularExpressions.Match;

internal sealed class AnalysisGameService(
    IAnalysisGameRepository repo,
    Matches.MatchesClient matchesClient,
    Moves.MovesClient movesClient)
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
        foreach (RegexMatch m in TagPattern.Matches(pgn))
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

    internal static string? MatchSanToUci(string san, IEnumerable<string> legalMoves, string fen)
    {
        san = san.TrimEnd('+', '#');

        if (san == "O-O-O")
        {
            return legalMoves.FirstOrDefault(m => m is "e1c1" or "e8c8");
        }

        if (san == "O-O")
        {
            return legalMoves.FirstOrDefault(m => m is "e1g1" or "e8g8");
        }

        string? promotionPiece = null;
        int eqIdx = san.IndexOf('=');
        if (eqIdx >= 0)
        {
            promotionPiece = char.ToLowerInvariant(san[eqIdx + 1]).ToString();
            san = san[..eqIdx];
        }

        string destSquare = san[^2..];
        string rest = san[..^2];

        string[] fenParts = fen.Split(' ');
        char activeColor = fenParts.Length >= 2 ? fenParts[1][0] : 'w';

        char fenPiece;
        string disambig;
        if (rest.Length > 0 && char.IsUpper(rest[0]))
        {
            fenPiece = activeColor == 'w' ? rest[0] : char.ToLowerInvariant(rest[0]);
            disambig = new string(rest[1..].Where(c => c != 'x').ToArray());
        }
        else
        {
            fenPiece = activeColor == 'w' ? 'P' : 'p';
            disambig = rest.Replace("x", string.Empty, StringComparison.Ordinal);
        }

        IEnumerable<string> candidates = legalMoves
            .Where(m => m.Length >= 4 && m[2..4] == destSquare);

        candidates = promotionPiece is not null
            ? candidates.Where(m => m.Length == 5 && m[4].ToString() == promotionPiece)
            : candidates.Where(m => m.Length == 4);

        candidates = candidates.Where(m => GetPieceAt(fen, m[..2]) == fenPiece);

        if (disambig.Length >= 2)
        {
            candidates = candidates.Where(m => m[..2] == disambig);
        }
        else if (disambig.Length == 1)
        {
            char d = disambig[0];
            candidates = char.IsLetter(d)
                ? candidates.Where(m => m[0] == d)
                : candidates.Where(m => m[1] == d);
        }

        return candidates.FirstOrDefault();
    }

    internal static char? GetPieceAt(string fen, string square)
    {
        if (square.Length < 2)
        {
            return null;
        }

        int file = square[0] - 'a';
        int rank = 8 - (square[1] - '0');

        string[] fenParts = fen.Split(' ');
        string[] rows = fenParts[0].Split('/');

        if (rank < 0 || rank >= 8 || file < 0 || file >= 8)
        {
            return null;
        }

        string row = rows[rank];
        int col = 0;
        foreach (char ch in row)
        {
            if (char.IsDigit(ch))
            {
                col += ch - '0';
            }
            else
            {
                if (col == file)
                {
                    return ch;
                }

                col++;
            }
        }

        return null;
    }

    internal async Task<AnalysisGame> GetGameAsync(string id, string userId, CancellationToken ct)
    {
        AnalysisGame game = await repo.GetByIdAsync(id, ct) ?? throw new AnalysisGameNotFoundException();
        return game.UserId != userId ? throw new AccessDeniedException() : game;
    }

    internal async Task<(IReadOnlyList<AnalysisGame> Games, int Total, int Page, int PageSize)> ListGamesAsync(
        string userId, int page, int pageSize, CancellationToken ct)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        page = Math.Max(1, page);
        int offset = (page - 1) * pageSize;

        Task<int> countTask = repo.CountByUserIdAsync(userId, ct);
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

        string currentFen = InitialFen;
        List<string> positionHistory = [];
        List<string> uciMoves = [];
        List<string> fens = [];

        foreach (string san in sanMoves)
        {
            GetLegalMovesResponse legalMovesResp = await movesClient.GetLegalMovesAsync(
                new GetLegalMovesRequest { Fen = currentFen },
                cancellationToken: ct);

            string uci = MatchSanToUci(san, legalMovesResp.Moves, currentFen)
                ?? throw new InvalidPgnException($"illegal or unrecognised move: {san}");

            ValidateMoveRequest validateReq = new() { Fen = currentFen, Move = uci };
            validateReq.PositionHistory.AddRange(positionHistory);
            ValidateMoveResponse validateResp = await movesClient.ValidateMoveAsync(
                validateReq, cancellationToken: ct);

            uciMoves.Add(uci);
            fens.Add(validateResp.ResultingFen);
            currentFen = validateResp.ResultingFen;
            positionHistory = [.. validateResp.PositionHistory];
        }

        AnalysisGame game = new(
            Id: string.Empty,
            UserId: userId,
            Source: "pgn",
            MatchId: null,
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

    internal async Task<AnalysisGame> ImportFromMatchAsync(string matchId, string userId, CancellationToken ct)
    {
        GetMatchResponse matchResp = await matchesClient.GetMatchAsync(
            new GetMatchRequest { MatchId = matchId },
            cancellationToken: ct);

        ProtoMatch match = matchResp.Match;

        if (match.Status == MatchStatus.Ongoing)
        {
            throw new MatchStillOngoingException();
        }

        bool isWhite = match.White.IdentityCase == Player.IdentityOneofCase.UserId
            && match.White.UserId == userId;
        bool isBlack = match.Black.IdentityCase == Player.IdentityOneofCase.UserId
            && match.Black.UserId == userId;

        if (!isWhite && !isBlack)
        {
            throw new MatchAccessDeniedException();
        }

        List<string> fens = [];
        for (int i = 1; i <= match.Moves.Count; i++)
        {
            GetMatchPositionResponse posResp = await matchesClient.GetMatchPositionAsync(
                new GetMatchPositionRequest { MatchId = matchId, Index = i },
                cancellationToken: ct);
            fens.Add(posResp.Fen);
        }

        Dictionary<string, string> whiteInfo = BuildPlayerInfo(match.White);
        Dictionary<string, string> blackInfo = BuildPlayerInfo(match.Black);

        string result = match.Status switch
        {
            MatchStatus.WhiteWon => "1-0",
            MatchStatus.BlackWon => "0-1",
            MatchStatus.Draw => "1/2-1/2",
            _ => "*",
        };

        AnalysisGame game = new(
            Id: string.Empty,
            UserId: userId,
            Source: "match",
            MatchId: matchId,
            Moves: [.. match.Moves],
            Fens: fens,
            Pgn: BuildMinimalPgn(match, whiteInfo, blackInfo, result),
            Result: result,
            White: whiteInfo,
            Black: blackInfo,
            Tags: BuildMatchTags(result),
            CreatedAt: DateTimeOffset.UtcNow);

        return await repo.InsertAsync(game, ct);
    }

    private static Dictionary<string, string> BuildPlayerInfo(Player player) =>
        player.IdentityCase switch
        {
            Player.IdentityOneofCase.UserId => new Dictionary<string, string> { ["user_id"] = player.UserId },
            Player.IdentityOneofCase.BotId => new Dictionary<string, string> { ["bot_id"] = player.BotId },
            _ => [],
        };

    private static string BuildMinimalPgn(
        ProtoMatch match,
        Dictionary<string, string> white,
        Dictionary<string, string> black,
        string result)
    {
        string whiteName = white.TryGetValue("user_id", out string? wid) ? wid
            : white.TryGetValue("bot_id", out string? wbid) ? wbid : "?";
        string blackName = black.TryGetValue("user_id", out string? bid) ? bid
            : black.TryGetValue("bot_id", out string? bbid) ? bbid : "?";

        StringBuilder sb = new();
        sb.AppendLine("[Event \"Maichess Match\"]");
        sb.AppendLine("[Site \"maichess\"]");
        sb.AppendLine($"[Date \"{DateTimeOffset.UtcNow.ToString("yyyy.MM.dd", CultureInfo.InvariantCulture)}\"]");
        sb.AppendLine($"[White \"{whiteName}\"]");
        sb.AppendLine($"[Black \"{blackName}\"]");
        sb.AppendLine($"[Result \"{result}\"]");
        sb.AppendLine();

        // Moves in UCI notation (SAN conversion not implemented; documented in CONTRACT_NOTES.md)
        int moveNum = 1;
        for (int i = 0; i < match.Moves.Count; i++)
        {
            if (i % 2 == 0)
            {
                sb.Append(CultureInfo.InvariantCulture, $"{moveNum++}. ");
            }

            sb.Append(match.Moves[i]);
            sb.Append(' ');
        }

        sb.Append(result);
        return sb.ToString();
    }

    private static Dictionary<string, string> BuildMatchTags(string result) =>
        new()
        {
            ["Event"] = "Maichess Match",
            ["Site"] = "maichess",
            ["Date"] = DateTimeOffset.UtcNow.ToString("yyyy.MM.dd", CultureInfo.InvariantCulture),
            ["Result"] = result,
        };
}
