using System.Diagnostics.CodeAnalysis;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Maichess.Database.V1;

namespace MaichessAnalysisService.Data;

[ExcludeFromCodeCoverage]
internal sealed class AnalysisMetaRepository(Database.DatabaseClient db)
{
    private const string Collection = "analysis_meta";
    private const string ConfigId = "config";

    internal async Task<string?> GetStoredBotIdAsync(CancellationToken ct)
    {
        try
        {
            GetResponse resp = await db.GetAsync(
                new GetRequest { Collection = Collection, Id = ConfigId },
                cancellationToken: ct);
            return resp.Record.Fields.TryGetValue("stored_bot_id", out Value? v)
                && v.KindCase == Value.KindOneofCase.StringValue
                ? v.StringValue
                : null;
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            return null;
        }
    }

    internal async Task UpsertStoredBotIdAsync(string botId, CancellationToken ct)
    {
        try
        {
            await db.UpdateAsync(
                new UpdateRequest
                {
                    Collection = Collection,
                    Id = ConfigId,
                    Fields = new Struct { Fields = { ["stored_bot_id"] = Value.ForString(botId) } },
                },
                cancellationToken: ct);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            await db.InsertAsync(
                new InsertRequest
                {
                    Collection = Collection,
                    Record = new Struct
                    {
                        Fields =
                        {
                            ["id"] = Value.ForString(ConfigId),
                            ["stored_bot_id"] = Value.ForString(botId),
                        },
                    },
                },
                cancellationToken: ct);
        }
    }
}
