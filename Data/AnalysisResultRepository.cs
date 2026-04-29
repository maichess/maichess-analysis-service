using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Google.Protobuf.WellKnownTypes;
using Maichess.Database.V1;
using MaichessAnalysisService.Domain;

namespace MaichessAnalysisService.Data;

[ExcludeFromCodeCoverage]
internal sealed class AnalysisResultRepository(Database.DatabaseClient db) : IAnalysisResultRepository
{
    private const string Collection = "analysis_results";

    public async Task<IReadOnlyList<AnalysisResultRecord>> GetCachedDepthsAsync(
        string fen, string botId, CancellationToken ct)
    {
        Struct filter = new()
        {
            Fields =
            {
                ["fen"] = Value.ForString(fen),
                ["bot_id"] = Value.ForString(botId),
            },
        };

        ListResponse response = await db.ListAsync(
            new ListRequest { Collection = Collection, Filter = filter, Limit = 100, Offset = 0 },
            cancellationToken: ct);

        return [.. response.Records.Select(FromStruct)];
    }

    public async Task InsertDepthAsync(AnalysisResultRecord record, CancellationToken ct)
    {
        await db.InsertAsync(
            new InsertRequest { Collection = Collection, Record = ToStruct(record) },
            cancellationToken: ct);
    }

    private static Struct ToStruct(AnalysisResultRecord r)
    {
        return new Struct
        {
            Fields =
            {
                ["id"] = Value.ForString(r.Id),
                ["fen"] = Value.ForString(r.Fen),
                ["bot_id"] = Value.ForString(r.BotId),
                ["line_count"] = Value.ForNumber(r.LineCount),
                ["depth"] = Value.ForNumber(r.Depth),
                ["lines"] = Value.ForList([.. r.Lines.Select(LineToValue)]),
                ["created_at"] = Value.ForString(r.CreatedAt.ToString("O", CultureInfo.InvariantCulture)),
            },
        };
    }

    private static Value LineToValue(AnalysisLine line) =>
        Value.ForStruct(new Struct
        {
            Fields =
            {
                ["rank"] = Value.ForNumber(line.Rank),
                ["evaluation_cp"] = Value.ForNumber(line.EvaluationCp),
                ["moves"] = Value.ForList([.. line.Moves.Select(Value.ForString)]),
            },
        });

    private static AnalysisResultRecord FromStruct(Struct s)
    {
        IReadOnlyList<AnalysisLine> lines = s.Fields.TryGetValue("lines", out Value? lv)
            && lv.KindCase == Value.KindOneofCase.ListValue
            ? [.. lv.ListValue.Values.Select(LineFromValue)]
            : [];

        return new AnalysisResultRecord(
            Id: s.Fields["id"].StringValue,
            Fen: s.Fields["fen"].StringValue,
            BotId: s.Fields["bot_id"].StringValue,
            LineCount: (int)s.Fields["line_count"].NumberValue,
            Depth: (int)s.Fields["depth"].NumberValue,
            Lines: lines,
            CreatedAt: DateTimeOffset.Parse(s.Fields["created_at"].StringValue, CultureInfo.InvariantCulture));
    }

    private static AnalysisLine LineFromValue(Value v)
    {
        Struct s = v.StructValue;
        IReadOnlyList<string> moves = s.Fields.TryGetValue("moves", out Value? mv)
            && mv.KindCase == Value.KindOneofCase.ListValue
            ? [.. mv.ListValue.Values.Select(m => m.StringValue)]
            : [];

        return new AnalysisLine(
            Rank: (int)s.Fields["rank"].NumberValue,
            EvaluationCp: (int)s.Fields["evaluation_cp"].NumberValue,
            Moves: moves);
    }
}
