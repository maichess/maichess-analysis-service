using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace MaichessAnalysisService.Rest;

[ExcludeFromCodeCoverage]
internal sealed record FromFenRequest(
    [property: JsonPropertyName("fen")] string Fen);
