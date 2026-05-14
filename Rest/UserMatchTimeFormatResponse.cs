using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace MaichessAnalysisService.Rest;

[ExcludeFromCodeCoverage]
internal sealed record UserMatchTimeFormatResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("base_ms")] long BaseMs,
    [property: JsonPropertyName("increment_ms")] long IncrementMs,
    [property: JsonPropertyName("category")] string Category);
