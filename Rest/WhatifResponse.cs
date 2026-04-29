using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace MaichessAnalysisService.Rest;

[ExcludeFromCodeCoverage]
internal sealed record WhatifResponse(
    [property: JsonPropertyName("whatif_index")] int WhatifIndex,
    [property: JsonPropertyName("current_fen")] string CurrentFen);
