using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;

namespace MaichessAnalysisService.Rest;

internal static class AnalysisEndpointHelpers
{
    internal static bool TryGetUserId(
        ClaimsPrincipal principal,
        [NotNullWhen(true)] out string? userId)
    {
        userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return userId is not null;
    }

    internal static IResult NotFoundResult() => Results.NotFound();

    internal static IResult ForbidResult() => Results.Forbid();

    internal static IResult InvalidPgnResult(string reason) =>
        Results.BadRequest(new ErrorResponse(reason));

    internal static IResult MatchOngoingResult() =>
        Results.BadRequest(new ErrorResponse("match is still ongoing"));
}
