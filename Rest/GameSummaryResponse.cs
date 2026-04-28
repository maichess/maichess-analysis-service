using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace MaichessAnalysisService.Rest;

[ExcludeFromCodeCoverage]
internal sealed record GameSummaryResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("match_id")] string? MatchId,
    [property: JsonPropertyName("white")] IReadOnlyDictionary<string, string> White,
    [property: JsonPropertyName("black")] IReadOnlyDictionary<string, string> Black,
    [property: JsonPropertyName("result")] string Result,
    [property: JsonPropertyName("move_count")] int MoveCount,
    [property: JsonPropertyName("created_at")] string CreatedAt,
    [property: JsonPropertyName("tags")] IReadOnlyDictionary<string, string> Tags);
