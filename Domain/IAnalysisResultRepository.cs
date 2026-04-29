namespace MaichessAnalysisService.Domain;

internal interface IAnalysisResultRepository
{
    Task<IReadOnlyList<AnalysisResultRecord>> GetCachedDepthsAsync(
        string fen, string botId, CancellationToken ct);

    Task InsertDepthAsync(AnalysisResultRecord record, CancellationToken ct);
}
