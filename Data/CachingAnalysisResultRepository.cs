using MaichessAnalysisService.Domain;
using MaichessAnalysisService.Services;
using Microsoft.Extensions.Options;

namespace MaichessAnalysisService.Data;

// L1-over-L2 decorator around the durable AnalysisResultRepository (Mongo). On a
// session-start lookup it checks the Redis L1 first, falls through to Mongo on a
// miss, and promotes the Mongo hit into L1; on a new default-bot depth write it
// persists to Mongo and appends to L1. Only the configured DefaultAnalysisBotId is
// cached — any other bot is served straight from Mongo, never touching L1. The L1 is
// rebuildable from Mongo, so the decorator is transparent to the session service.
// See caching-and-read-models.md (Part A).
internal sealed class CachingAnalysisResultRepository(
    IAnalysisResultRepository inner,
    IAnalysisResultCache l1,
    IOptions<AnalysisConfig> configOptions) : IAnalysisResultRepository
{
    private readonly string defaultBotId = configOptions.Value.DefaultBotId;

    public async Task<IReadOnlyList<AnalysisResultRecord>> GetCachedDepthsAsync(
        string fen, string botId, CancellationToken ct)
    {
        if (botId != defaultBotId)
        {
            return await inner.GetCachedDepthsAsync(fen, botId, ct);
        }

        IReadOnlyList<AnalysisResultRecord>? cached = await l1.GetAsync(botId, fen, ct);
        if (cached is not null)
        {
            return cached;
        }

        IReadOnlyList<AnalysisResultRecord> fromL2 = await inner.GetCachedDepthsAsync(fen, botId, ct);
        if (fromL2.Count > 0)
        {
            await l1.SetAsync(botId, fen, fromL2, ct);
        }

        return fromL2;
    }

    public async Task InsertDepthAsync(AnalysisResultRecord record, CancellationToken ct)
    {
        await inner.InsertDepthAsync(record, ct);
        if (record.BotId == defaultBotId)
        {
            await l1.AppendAsync(record.BotId, record.Fen, record, ct);
        }
    }
}
