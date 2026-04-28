namespace MaichessAnalysisService.Domain;

internal interface IAnalysisGameRepository
{
    Task<AnalysisGame?> GetByIdAsync(string id, CancellationToken ct);

    Task<IReadOnlyList<AnalysisGame>> ListByUserIdAsync(
        string userId, int limit, int offset, CancellationToken ct);

    Task<int> CountByUserIdAsync(string userId, CancellationToken ct);

    Task<AnalysisGame> InsertAsync(AnalysisGame game, CancellationToken ct);

    Task DeleteAsync(string id, CancellationToken ct);
}
