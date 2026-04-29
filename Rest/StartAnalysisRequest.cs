using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace MaichessAnalysisService.Rest;

[ExcludeFromCodeCoverage]
internal sealed record StartAnalysisRequest(
    [property: JsonPropertyName("bot_id")] string? BotId,
    [property: JsonPropertyName("line_count")] int? LineCount);
