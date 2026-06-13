using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Text.Json;
using MaichessAnalysisService.Domain;
using StackExchange.Redis;

namespace MaichessAnalysisService.Data;

// StackExchange.Redis implementation of the analysis-results L1. Each default-bot
// position is a hash at analysis:{botId}:{fen} whose fields are depths (string) and
// whose values are the JSON-serialised depth records. No expiry (allkeys-lru only);
// every key is rebuildable from the Mongo L2 on a miss. Excluded from coverage like
// the repositories: it requires a live Redis and is exercised through the caching
// repository against a mocked IAnalysisResultCache.
[ExcludeFromCodeCoverage]
internal sealed class RedisAnalysisResultCache(IConnectionMultiplexer redis) : IAnalysisResultCache
{
    private const string KeyPrefix = "analysis:";

    private IDatabase Db => redis.GetDatabase();

    public async Task<IReadOnlyList<AnalysisResultRecord>?> GetAsync(
        string botId, string fen, CancellationToken ct)
    {
        HashEntry[] fields = await Db.HashGetAllAsync(Key(botId, fen));
        return fields.Length == 0
            ? null
            : [.. fields
                .Select(f => JsonSerializer.Deserialize<AnalysisResultRecord>((string)f.Value!))
                .Where(r => r is not null)
                .Select(r => r!)];
    }

    public async Task SetAsync(
        string botId, string fen, IReadOnlyList<AnalysisResultRecord> records, CancellationToken ct)
    {
        HashEntry[] entries =
        [
            .. records.Select(r => new HashEntry(
                r.Depth.ToString(CultureInfo.InvariantCulture),
                JsonSerializer.Serialize(r))),
        ];
        await Db.HashSetAsync(Key(botId, fen), entries);
    }

    public async Task AppendAsync(
        string botId, string fen, AnalysisResultRecord record, CancellationToken ct) =>
        await Db.HashSetAsync(
            Key(botId, fen),
            record.Depth.ToString(CultureInfo.InvariantCulture),
            JsonSerializer.Serialize(record));

    public async Task ClearAllAsync(CancellationToken ct)
    {
        // SCAN the analysis:* keyspace rather than tracking an index set: under
        // allkeys-lru an index could be evicted independently of the keys it
        // references, leaking stale entries. Mirrors RedisMatchCache eviction.
        RedisValue pattern = $"{KeyPrefix}*";
        foreach (EndPoint endpoint in redis.GetEndPoints())
        {
            IServer server = redis.GetServer(endpoint);
            if (server.IsReplica)
            {
                continue;
            }

            await foreach (RedisKey key in server.KeysAsync(pattern: pattern).WithCancellation(ct))
            {
                await Db.KeyDeleteAsync(key);
            }
        }
    }

    private static string Key(string botId, string fen) => $"{KeyPrefix}{botId}:{fen}";
}
