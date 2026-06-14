using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using Grpc.Core;
using Maichess.Engine.V1;
using MaichessAnalysisService.Domain;
using MaichessAnalysisService.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace MaichessAnalysisService.Rest;

[ExcludeFromCodeCoverage]
internal static class AnalysisEndpoints
{
    internal static IEndpointRouteBuilder MapAnalysisEndpoints(this IEndpointRouteBuilder routes)
    {
        RouteGroupBuilder games = routes.MapGroup("/games").RequireAuthorization();
        games.MapGet(string.Empty, ListGames);
        games.MapGet("/{id}", GetGame);
        games.MapPost(string.Empty, ImportPgn);
        games.MapPost("/from-match/{matchId}", ImportFromMatch);
        games.MapPost("/from-fen", ImportFromFen);
        games.MapDelete("/{id}", DeleteGame);

        RouteGroupBuilder matches = routes.MapGroup("/matches").RequireAuthorization();
        matches.MapGet(string.Empty, ListUserMatches);

        RouteGroupBuilder analysis = routes.MapGroup("/analysis").RequireAuthorization();
        analysis.MapGet("/config", GetAnalysisConfig);

        RouteGroupBuilder sessions = routes.MapGroup("/sessions").RequireAuthorization();
        sessions.MapPost(string.Empty, CreateSession);
        sessions.MapDelete("/{id}", DestroySession);
        sessions.MapPost("/{id}/navigate", Navigate);
        sessions.MapPost("/{id}/whatif", PlayWhatif);
        sessions.MapDelete("/{id}/whatif", ResetWhatif);
        sessions.MapDelete("/{id}/whatif/last", UndoLastWhatif);
        sessions.MapGet("/{id}/whatif/pgn", GetWhatifPgn);
        sessions.MapPost("/{id}/analysis", StartAnalysis);
        sessions.MapDelete("/{id}/analysis", StopAnalysis);

        return routes;
    }

    private static async Task<IResult> ListUserMatches(
        ClaimsPrincipal principal,
        AnalysisGameService service,
        [FromQuery] string? status = null,
        [FromQuery] int page = 1,
        [FromQuery(Name = "page_size")] int pageSize = 20,
        CancellationToken ct = default)
    {
        if (!AnalysisEndpointHelpers.TryGetUserId(principal, out string? userId))
        {
            return Results.Unauthorized();
        }

        UserMatchStatusFilter filter;
        try
        {
            filter = AnalysisGameService.ParseStatusFilter(status);
        }
        catch (InvalidMatchStatusFilterException ex)
        {
            return AnalysisEndpointHelpers.InvalidStatusResult(ex.Value);
        }

        (IReadOnlyList<UserMatchSummary> matches, long total, int p, int ps) =
            await service.ListUserMatchesAsync(userId, filter, page, pageSize, ct);

        return Results.Ok(new UserMatchesListResponse(
            [.. matches.Select(AnalysisGameMapper.ToUserMatchSummary)],
            total,
            p,
            ps));
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

        (IReadOnlyList<AnalysisGame> games, long total, int p, int ps) =
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
        catch (AnalysisGameNotFoundException)
        {
            return AnalysisEndpointHelpers.NotFoundResult();
        }
        catch (MatchAccessDeniedException)
        {
            return AnalysisEndpointHelpers.ForbidResult();
        }
    }

    private static async Task<IResult> ImportFromFen(
        [FromBody] FromFenRequest body,
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
            AnalysisGame game = await service.ImportFromFenAsync(body.Fen, userId, ct);
            return Results.Created($"/games/{game.Id}", AnalysisGameMapper.ToDetail(game));
        }
        catch (InvalidPgnException ex)
        {
            return AnalysisEndpointHelpers.InvalidPgnResult(ex.Reason);
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

    private static async Task<IResult> GetAnalysisConfig(
        Bots.BotsClient botsClient,
        IOptions<AnalysisConfig> configOptions,
        CancellationToken ct)
    {
        ListBotsResponse resp = await botsClient.ListBotsAsync(
            new ListBotsRequest(), cancellationToken: ct);

        AnalysisConfig config = configOptions.Value;
        return Results.Ok(new AnalysisConfigResponse(
            config.DefaultBotId,
            config.DefaultLineCount,
            [.. resp.Bots.Select(b => new BotInfoResponse(b.Id, b.Name, b.Elo))]));
    }

    private static async Task<IResult> CreateSession(
        [FromBody] CreateSessionRequest body,
        ClaimsPrincipal principal,
        AnalysisSessionService service,
        CancellationToken ct)
    {
        if (!AnalysisEndpointHelpers.TryGetUserId(principal, out string? userId))
        {
            return Results.Unauthorized();
        }

        try
        {
            Domain.AnalysisSession session = await service.CreateSessionAsync(
                userId, body.GameId, body.BotId, body.LineCount, ct);
            return Results.Created($"/sessions/{session.Id}", SessionResponse.FromSession(session));
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

    private static Task<IResult> DestroySession(
        string id,
        ClaimsPrincipal principal,
        AnalysisSessionService service,
        CancellationToken ct)
    {
        if (!AnalysisEndpointHelpers.TryGetUserId(principal, out string? userId))
        {
            return Task.FromResult(Results.Unauthorized());
        }

        try
        {
            service.DestroySessionAsync(id, userId, ct);
            return Task.FromResult(Results.NoContent());
        }
        catch (SessionNotFoundException)
        {
            return Task.FromResult(AnalysisEndpointHelpers.SessionNotFoundResult());
        }
    }

    private static async Task<IResult> Navigate(
        string id,
        [FromBody] NavigateRequest body,
        ClaimsPrincipal principal,
        AnalysisSessionService service,
        CancellationToken ct)
    {
        if (!AnalysisEndpointHelpers.TryGetUserId(principal, out string? userId))
        {
            return Results.Unauthorized();
        }

        try
        {
            (int index, string fen) = await service.NavigateAsync(id, userId, body.Index, ct);
            return Results.Ok(new NavigateResponse(index, fen));
        }
        catch (SessionNotFoundException)
        {
            return AnalysisEndpointHelpers.SessionNotFoundResult();
        }
        catch (NavigationOutOfRangeException)
        {
            return AnalysisEndpointHelpers.NavigationOutOfRangeResult();
        }
    }

    private static async Task<IResult> PlayWhatif(
        string id,
        [FromBody] WhatifRequest body,
        ClaimsPrincipal principal,
        AnalysisSessionService service,
        CancellationToken ct)
    {
        if (!AnalysisEndpointHelpers.TryGetUserId(principal, out string? userId))
        {
            return Results.Unauthorized();
        }

        try
        {
            (int whatifIndex, string fen) = await service.PlayWhatifAsync(id, userId, body.Move, ct);
            return Results.Ok(new WhatifResponse(whatifIndex, fen));
        }
        catch (SessionNotFoundException)
        {
            return AnalysisEndpointHelpers.SessionNotFoundResult();
        }
        catch (InvalidWhatifMoveException ex)
        {
            return AnalysisEndpointHelpers.InvalidWhatifMoveResult(ex.Reason);
        }
    }

    private static async Task<IResult> ResetWhatif(
        string id,
        ClaimsPrincipal principal,
        AnalysisSessionService service,
        CancellationToken ct)
    {
        if (!AnalysisEndpointHelpers.TryGetUserId(principal, out string? userId))
        {
            return Results.Unauthorized();
        }

        try
        {
            (int index, string fen) = await service.ResetWhatifAsync(id, userId, ct);
            return Results.Ok(new NavigateResponse(index, fen));
        }
        catch (SessionNotFoundException)
        {
            return AnalysisEndpointHelpers.SessionNotFoundResult();
        }
    }

    private static async Task<IResult> UndoLastWhatif(
        string id,
        ClaimsPrincipal principal,
        AnalysisSessionService service,
        CancellationToken ct)
    {
        if (!AnalysisEndpointHelpers.TryGetUserId(principal, out string? userId))
        {
            return Results.Unauthorized();
        }

        try
        {
            (int whatifIndex, string fen) = await service.UndoLastWhatifAsync(id, userId, ct);
            return Results.Ok(new WhatifResponse(whatifIndex, fen));
        }
        catch (SessionNotFoundException)
        {
            return AnalysisEndpointHelpers.SessionNotFoundResult();
        }
        catch (WhatifEmptyException)
        {
            return AnalysisEndpointHelpers.WhatifEmptyResult();
        }
    }

    private static async Task<IResult> GetWhatifPgn(
        string id,
        ClaimsPrincipal principal,
        AnalysisSessionService service,
        CancellationToken ct)
    {
        if (!AnalysisEndpointHelpers.TryGetUserId(principal, out string? userId))
        {
            return Results.Unauthorized();
        }

        try
        {
            string pgn = await service.GetWhatifPgnAsync(id, userId, ct);
            return Results.Ok(new WhatifPgnResponse(pgn));
        }
        catch (SessionNotFoundException)
        {
            return AnalysisEndpointHelpers.SessionNotFoundResult();
        }
        catch (WhatifEmptyException)
        {
            return AnalysisEndpointHelpers.WhatifEmptyResult();
        }
    }

    private static async Task<IResult> StartAnalysis(
        string id,
        [FromBody] StartAnalysisRequest body,
        ClaimsPrincipal principal,
        AnalysisSessionService service,
        CancellationToken ct)
    {
        if (!AnalysisEndpointHelpers.TryGetUserId(principal, out string? userId))
        {
            return Results.Unauthorized();
        }

        try
        {
            await service.StartAnalysisAsync(id, userId, body.BotId, body.LineCount, ct);
            return Results.NoContent();
        }
        catch (SessionNotFoundException)
        {
            return AnalysisEndpointHelpers.SessionNotFoundResult();
        }
    }

    private static Task<IResult> StopAnalysis(
        string id,
        ClaimsPrincipal principal,
        AnalysisSessionService service,
        CancellationToken ct)
    {
        if (!AnalysisEndpointHelpers.TryGetUserId(principal, out string? userId))
        {
            return Task.FromResult(Results.Unauthorized());
        }

        try
        {
            service.StopAnalysisAsync(id, userId, ct);
            return Task.FromResult(Results.NoContent());
        }
        catch (SessionNotFoundException)
        {
            return Task.FromResult(AnalysisEndpointHelpers.SessionNotFoundResult());
        }
    }
}
