using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Maichess.Database.V1;
using MaichessAnalysisService.Domain;

namespace MaichessAnalysisService.Data;

[ExcludeFromCodeCoverage]
internal sealed class AnalysisGameRepository(Database.DatabaseClient db) : IAnalysisGameRepository
{
    private const string Collection = "analysis_games";

    public async Task<AnalysisGame?> GetByIdAsync(string id, CancellationToken ct)
    {
        try
        {
            GetResponse response = await db.GetAsync(
                new GetRequest { Collection = Collection, Id = id },
                cancellationToken: ct);
            return FromStruct(response.Record);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<AnalysisGame>> ListByUserIdAsync(
        string userId, int limit, int offset, CancellationToken ct)
    {
        Struct filter = new() { Fields = { ["user_id"] = Value.ForString(userId) } };

        ListResponse response = await db.ListAsync(
            new ListRequest { Collection = Collection, Filter = filter, Limit = limit, Offset = offset },
            cancellationToken: ct);

        return [.. response.Records.Select(FromStruct)];
    }

    public async Task<int> CountByUserIdAsync(string userId, CancellationToken ct)
    {
        Struct filter = new() { Fields = { ["user_id"] = Value.ForString(userId) } };

        ListResponse response = await db.ListAsync(
            new ListRequest { Collection = Collection, Filter = filter, Limit = 10000, Offset = 0 },
            cancellationToken: ct);

        return response.Records.Count;
    }

    public async Task<AnalysisGame> InsertAsync(AnalysisGame game, CancellationToken ct)
    {
        InsertResponse response = await db.InsertAsync(
            new InsertRequest { Collection = Collection, Record = ToStruct(game) },
            cancellationToken: ct);
        return FromStruct(response.Record);
    }

    public async Task DeleteAsync(string id, CancellationToken ct)
    {
        try
        {
            await db.DeleteAsync(
                new DeleteRequest { Collection = Collection, Id = id },
                cancellationToken: ct);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            throw new AnalysisGameNotFoundException();
        }
    }

    private static Struct ToStruct(AnalysisGame game)
    {
        Struct s = new()
        {
            Fields =
            {
                ["id"] = Value.ForString(game.Id),
                ["user_id"] = Value.ForString(game.UserId),
                ["source"] = Value.ForString(game.Source),
                ["match_id"] = game.MatchId is not null ? Value.ForString(game.MatchId) : Value.ForNull(),
                ["moves"] = Value.ForList(game.Moves.Select(Value.ForString).ToArray()),
                ["fens"] = Value.ForList(game.Fens.Select(Value.ForString).ToArray()),
                ["pgn"] = Value.ForString(game.Pgn),
                ["result"] = Value.ForString(game.Result),
                ["white"] = Value.ForStruct(DictToStruct(game.White)),
                ["black"] = Value.ForStruct(DictToStruct(game.Black)),
                ["tags"] = Value.ForStruct(DictToStruct(game.Tags)),
                ["created_at"] = Value.ForString(
                    game.CreatedAt.ToString("O", CultureInfo.InvariantCulture)),
            },
        };
        return s;
    }

    private static AnalysisGame FromStruct(Struct s)
    {
        bool hasMatchId = s.Fields.TryGetValue("match_id", out Value? mid)
            && mid.KindCase == Value.KindOneofCase.StringValue;

        return new AnalysisGame(
            Id: s.Fields["id"].StringValue,
            UserId: s.Fields["user_id"].StringValue,
            Source: s.Fields["source"].StringValue,
            MatchId: hasMatchId ? mid!.StringValue : null,
            Moves: [.. s.Fields["moves"].ListValue.Values.Select(v => v.StringValue)],
            Fens: [.. s.Fields["fens"].ListValue.Values.Select(v => v.StringValue)],
            Pgn: s.Fields["pgn"].StringValue,
            Result: s.Fields["result"].StringValue,
            White: StructToDict(s.Fields["white"].StructValue),
            Black: StructToDict(s.Fields["black"].StructValue),
            Tags: StructToDict(s.Fields["tags"].StructValue),
            CreatedAt: DateTimeOffset.Parse(
                s.Fields["created_at"].StringValue, CultureInfo.InvariantCulture));
    }

    private static Struct DictToStruct(IReadOnlyDictionary<string, string> dict)
    {
        Struct s = new();
        foreach ((string key, string val) in dict)
        {
            s.Fields[key] = Value.ForString(val);
        }

        return s;
    }

    private static Dictionary<string, string> StructToDict(Struct s) =>
        s.Fields
            .Where(kvp => kvp.Value.KindCase == Value.KindOneofCase.StringValue)
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value.StringValue);
}
