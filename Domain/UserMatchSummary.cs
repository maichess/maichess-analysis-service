namespace MaichessAnalysisService.Domain;

internal sealed record UserMatchSummary(
    string MatchId,
    IReadOnlyDictionary<string, string> White,
    IReadOnlyDictionary<string, string> Black,
    string Status,
    UserMatchTimeFormat TimeFormat,
    int MoveCount,
    long FinishedAtMs);
