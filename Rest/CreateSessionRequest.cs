using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace MaichessAnalysisService.Rest;

[ExcludeFromCodeCoverage]
internal sealed record CreateSessionRequest(
    [property: JsonPropertyName("game_id")] string GameId,
    [property: JsonPropertyName("bot_id")] string BotId,
    [property: JsonPropertyName("line_count")] int LineCount);
