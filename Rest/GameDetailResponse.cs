using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace MaichessAnalysisService.Rest;

[ExcludeFromCodeCoverage]
internal sealed record GameDetailResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("starting_fen")] string StartingFen,
    [property: JsonPropertyName("match_id")] string? MatchId,
    [property: JsonPropertyName("white")] IReadOnlyDictionary<string, string> White,
    [property: JsonPropertyName("black")] IReadOnlyDictionary<string, string> Black,
    [property: JsonPropertyName("result")] string Result,
    [property: JsonPropertyName("move_count")] int MoveCount,
    [property: JsonPropertyName("created_at")] string CreatedAt,
    [property: JsonPropertyName("tags")] IReadOnlyDictionary<string, string> Tags,
    [property: JsonPropertyName("moves")] IReadOnlyList<string> Moves,
    [property: JsonPropertyName("fens")] IReadOnlyList<string> Fens,
    [property: JsonPropertyName("pgn")] string Pgn);
