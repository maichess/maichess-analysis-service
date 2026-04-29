using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace MaichessAnalysisService.Rest;

[ExcludeFromCodeCoverage]
internal sealed record AnalysisConfigResponse(
    [property: JsonPropertyName("default_bot_id")] string DefaultBotId,
    [property: JsonPropertyName("default_line_count")] int DefaultLineCount,
    [property: JsonPropertyName("bots")] IReadOnlyList<BotInfoResponse> Bots);
