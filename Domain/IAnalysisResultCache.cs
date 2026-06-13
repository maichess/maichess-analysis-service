namespace MaichessAnalysisService.Domain;

// Redis L1 in front of the durable Mongo analysis_results cache (L2). Holds the
// depth-ordered lines for a default-bot position at analysis:{botId}:{fen} (a hash
// keyed by depth). Append-only data with no expiry — entries are rebuildable from
// L2 and rely on allkeys-lru eviction, so a cold miss simply falls through to Mongo.
// Only the configured DefaultAnalysisBotId is ever cached here. See
// caching-and-read-models.md (Part A).
internal interface IAnalysisResultCache
{
    // Returns the cached depth records for a position, or null on a miss (key absent).
    Task<IReadOnlyList<AnalysisResultRecord>?> GetAsync(
        string botId, string fen, CancellationToken ct);

    // Promotes a full set of L2 depth records into L1 in one shot (on a Mongo hit).
    Task SetAsync(
        string botId, string fen, IReadOnlyList<AnalysisResultRecord> records, CancellationToken ct);

    // Appends a single freshly-computed depth record to L1 (on a new engine write).
    Task AppendAsync(
        string botId, string fen, AnalysisResultRecord record, CancellationToken ct);

    // Drops every cached position. Called by the startup bot-change scrape so stale
    // analysis from a previous default bot never survives in L1.
    Task ClearAllAsync(CancellationToken ct);
}
