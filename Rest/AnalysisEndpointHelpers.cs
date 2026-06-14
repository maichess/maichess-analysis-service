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

    internal static IResult InvalidStatusResult(string value) =>
        Results.BadRequest(new ErrorResponse($"invalid status: {value}"));

    internal static IResult SessionNotFoundResult() => Results.NotFound();

    internal static IResult WhatifEmptyResult() =>
        Results.BadRequest(new ErrorResponse("no whatif moves"));

    internal static IResult NavigationOutOfRangeResult() =>
        Results.BadRequest(new ErrorResponse("navigation index out of range"));

    internal static IResult InvalidWhatifMoveResult(string reason) =>
        Results.BadRequest(new ErrorResponse(reason));
}
