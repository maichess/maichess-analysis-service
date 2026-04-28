using System.Security.Claims;
using MaichessAnalysisService.Rest;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Xunit;

namespace MaichessAnalysisService.Tests;

public sealed class AnalysisEndpointHelpersTests
{
    [Fact]
    public void TryGetUserId_WithNameIdentifierClaim_ReturnsTrueAndExtractsUserId()
    {
        ClaimsPrincipal principal = new(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "user-1")]));

        bool result = AnalysisEndpointHelpers.TryGetUserId(principal, out string? userId);

        Assert.True(result);
        Assert.Equal("user-1", userId);
    }

    [Fact]
    public void TryGetUserId_WithoutNameIdentifierClaim_ReturnsFalseAndNullUserId()
    {
        ClaimsPrincipal principal = new(new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, "user-1")]));

        bool result = AnalysisEndpointHelpers.TryGetUserId(principal, out string? userId);

        Assert.False(result);
        Assert.Null(userId);
    }

    [Fact]
    public void NotFoundResult_Returns404StatusCode()
    {
        IResult result = AnalysisEndpointHelpers.NotFoundResult();

        IStatusCodeHttpResult statusResult = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(404, statusResult.StatusCode);
    }

    [Fact]
    public void ForbidResult_ReturnsForbidHttpResult()
    {
        IResult result = AnalysisEndpointHelpers.ForbidResult();

        Assert.IsType<ForbidHttpResult>(result);
    }

    [Fact]
    public void InvalidPgnResult_Returns400WithErrorReason()
    {
        IResult result = AnalysisEndpointHelpers.InvalidPgnResult("illegal move: Nf3");

        BadRequest<ErrorResponse> badRequest = Assert.IsType<BadRequest<ErrorResponse>>(result);
        Assert.Equal(400, ((IStatusCodeHttpResult)badRequest).StatusCode);
        Assert.Equal("illegal move: Nf3", badRequest.Value!.Error);
    }

    [Fact]
    public void MatchOngoingResult_Returns400WithMatchOngoingMessage()
    {
        IResult result = AnalysisEndpointHelpers.MatchOngoingResult();

        BadRequest<ErrorResponse> badRequest = Assert.IsType<BadRequest<ErrorResponse>>(result);
        Assert.Equal(400, ((IStatusCodeHttpResult)badRequest).StatusCode);
        Assert.Equal("match is still ongoing", badRequest.Value!.Error);
    }
}
