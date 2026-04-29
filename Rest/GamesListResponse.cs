using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace MaichessAnalysisService.Rest;

[ExcludeFromCodeCoverage]
internal sealed record GamesListResponse(
    [property: JsonPropertyName("games")] IReadOnlyList<GameSummaryResponse> Games,
    [property: JsonPropertyName("total")] long Total,
    [property: JsonPropertyName("page")] int Page,
    [property: JsonPropertyName("page_size")] int PageSize);
