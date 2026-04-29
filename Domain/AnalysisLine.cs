namespace MaichessAnalysisService.Domain;

internal sealed record AnalysisLine(
    int Rank,
    int EvaluationCp,
    IReadOnlyList<string> Moves);
