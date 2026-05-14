namespace MaichessAnalysisService.Domain;

internal sealed record UserMatchTimeFormat(
    string Id,
    long BaseMs,
    long IncrementMs,
    string Category);
