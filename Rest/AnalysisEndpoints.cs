using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using Grpc.Core;
using MaichessAnalysisService.Domain;
using MaichessAnalysisService.Services;
using Microsoft.AspNetCore.Mvc;

namespace MaichessAnalysisService.Rest;

[ExcludeFromCodeCoverage]
internal static class AnalysisEndpoints
{
    internal static IEndpointRouteBuilder MapAnalysisEndpoints(this IEndpointRouteBuilder routes)
    {
        RouteGroupBuilder group = routes.MapGroup("/games").RequireAuthorization();

        group.MapGet(string.Empty, ListGames);
        group.MapGet("/{id}", GetGame);
        group.MapPost(string.Empty, ImportPgn);
        group.MapPost("/from-match/{matchId}", ImportFromMatch);
        group.MapDelete("/{id}", DeleteGame);

        return routes;
    }

    private static async Task<IResult> ListGames(
        ClaimsPrincipal principal,
        AnalysisGameService service,
        [FromQuery] int page = 1,
        [FromQuery(Name = "page_size")] int pageSize = 20,
        CancellationToken ct = default)
    {
        if (!AnalysisEndpointHelpers.TryGetUserId(principal, out string? userId))
        {
            return Results.Unauthorized();
        }

        (IReadOnlyList<AnalysisGame> games, int total, int p, int ps) =
            await service.ListGamesAsync(userId, page, pageSize, ct);

        return Results.Ok(new GamesListResponse(
            [.. games.Select(AnalysisGameMapper.ToSummary)],
            total,
            p,
            ps));
    }

    private static async Task<IResult> GetGame(
        string id,
        ClaimsPrincipal principal,
        AnalysisGameService service,
        CancellationToken ct)
    {
        if (!AnalysisEndpointHelpers.TryGetUserId(principal, out string? userId))
        {
            return Results.Unauthorized();
        }

        try
        {
            AnalysisGame game = await service.GetGameAsync(id, userId, ct);
            return Results.Ok(AnalysisGameMapper.ToDetail(game));
        }
        catch (AnalysisGameNotFoundException)
        {
            return AnalysisEndpointHelpers.NotFoundResult();
        }
        catch (AccessDeniedException)
        {
            return AnalysisEndpointHelpers.ForbidResult();
        }
    }

    private static async Task<IResult> ImportPgn(
        [FromBody] ImportPgnRequest body,
        ClaimsPrincipal principal,
        AnalysisGameService service,
        CancellationToken ct)
    {
        if (!AnalysisEndpointHelpers.TryGetUserId(principal, out string? userId))
        {
            return Results.Unauthorized();
        }

        try
        {
            AnalysisGame game = await service.ImportFromPgnAsync(body.Pgn, userId, ct);
            return Results.Created($"/games/{game.Id}", AnalysisGameMapper.ToDetail(game));
        }
        catch (InvalidPgnException ex)
        {
            return AnalysisEndpointHelpers.InvalidPgnResult(ex.Reason);
        }
    }

    private static async Task<IResult> ImportFromMatch(
        string matchId,
        ClaimsPrincipal principal,
        AnalysisGameService service,
        CancellationToken ct)
    {
        if (!AnalysisEndpointHelpers.TryGetUserId(principal, out string? userId))
        {
            return Results.Unauthorized();
        }

        try
        {
            AnalysisGame game = await service.ImportFromMatchAsync(matchId, userId, ct);
            return Results.Created($"/games/{game.Id}", AnalysisGameMapper.ToDetail(game));
        }
        catch (MatchStillOngoingException)
        {
            return AnalysisEndpointHelpers.MatchOngoingResult();
        }
        catch (MatchAccessDeniedException)
        {
            return AnalysisEndpointHelpers.ForbidResult();
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            return AnalysisEndpointHelpers.NotFoundResult();
        }
    }

    private static async Task<IResult> DeleteGame(
        string id,
        ClaimsPrincipal principal,
        AnalysisGameService service,
        CancellationToken ct)
    {
        if (!AnalysisEndpointHelpers.TryGetUserId(principal, out string? userId))
        {
            return Results.Unauthorized();
        }

        try
        {
            await service.DeleteGameAsync(id, userId, ct);
            return Results.NoContent();
        }
        catch (AnalysisGameNotFoundException)
        {
            return AnalysisEndpointHelpers.NotFoundResult();
        }
        catch (AccessDeniedException)
        {
            return AnalysisEndpointHelpers.ForbidResult();
        }
    }
}
