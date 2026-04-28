namespace MaichessAnalysisService.Domain;

internal sealed record AnalysisGame(
    string Id,
    string UserId,
    string Source,
    string? MatchId,
    IReadOnlyList<string> Moves,
    IReadOnlyList<string> Fens,
    string Pgn,
    string Result,
    IReadOnlyDictionary<string, string> White,
    IReadOnlyDictionary<string, string> Black,
    IReadOnlyDictionary<string, string> Tags,
    DateTimeOffset CreatedAt);
