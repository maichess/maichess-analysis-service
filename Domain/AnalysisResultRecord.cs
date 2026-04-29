namespace MaichessAnalysisService.Domain;

internal sealed record AnalysisResultRecord(
    string Id,
    string Fen,
    string BotId,
    int LineCount,
    int Depth,
    IReadOnlyList<AnalysisLine> Lines,
    DateTimeOffset CreatedAt);
