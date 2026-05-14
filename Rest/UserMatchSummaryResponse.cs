using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace MaichessAnalysisService.Rest;

[ExcludeFromCodeCoverage]
internal sealed record UserMatchSummaryResponse(
    [property: JsonPropertyName("match_id")] string MatchId,
    [property: JsonPropertyName("white")] IReadOnlyDictionary<string, string> White,
    [property: JsonPropertyName("black")] IReadOnlyDictionary<string, string> Black,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("time_format")] UserMatchTimeFormatResponse TimeFormat,
    [property: JsonPropertyName("move_count")] int MoveCount,
    [property: JsonPropertyName("finished_at_ms")] long FinishedAtMs);
