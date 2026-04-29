using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace MaichessAnalysisService.Rest;

[ExcludeFromCodeCoverage]
internal sealed record NavigateResponse(
    [property: JsonPropertyName("current_index")] int CurrentIndex,
    [property: JsonPropertyName("current_fen")] string CurrentFen);
