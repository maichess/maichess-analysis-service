using Grpc.Core;
using Maichess.MatchManager.V1;
using MaichessAnalysisService.Domain;
using MaichessAnalysisService.Tests.Support;
using NSubstitute;
using Reqnroll;
using Xunit;

namespace MaichessAnalysisService.Tests.StepDefinitions;

[Binding]
internal sealed class AnalysisGameServiceSteps(AnalysisServiceContext context)
{
    private const string InitialFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    // ── Given ────────────────────────────────────────────────────────────────

    [Given(@"a saved game ""([^""]*)"" owned by ""([^""]*)""")]
    public void GivenASavedGameOwnedBy(string id, string userId)
    {
        AnalysisGame game = AnalysisServiceContext.BuildGame(id, userId);
        context.SetupGame(game);
        context.Repository.DeleteAsync(id, Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
    }

    [Given(@"game ""([^""]*)"" does not exist")]
    public void GivenGameDoesNotExist(string id) =>
        context.SetupGameNotFound(id);

    [Given(@"""([^""]*)"" has (\d+) saved games")]
    public void GivenUserHasSavedGames(string userId, int count)
    {
        List<AnalysisGame> games = Enumerable.Range(1, count)
            .Select(i => AnalysisServiceContext.BuildGame($"game-{i}", userId))
            .ToList();
        context.SetupList(userId, games, count);
    }

    [Given(@"the move validator resolves ""([^""]*)"" at the initial FEN to ""([^""]*)""")]
    public void GivenMoveValidatorResolvesAtInitialFen(string move, string resultingFen)
    {
        context.SetupLegalMoves(InitialFen, [move]);
        context.SetupValidateMove(InitialFen, move, resultingFen);
    }

    [Given(@"the move validator resolves ""([^""]*)"" at ""([^""]*)"" to ""([^""]*)""")]
    public void GivenMoveValidatorResolvesAtFen(string move, string fen, string resultingFen)
    {
        context.SetupLegalMoves(fen, [move]);
        context.SetupValidateMove(fen, move, resultingFen);
    }

    [Given(@"the move validator returns no legal moves at the initial FEN")]
    public void GivenMoveValidatorReturnsNoLegalMovesAtInitialFen() =>
        context.SetupLegalMoves(InitialFen, []);

    [Given(@"match ""([^""]*)"" is finished with white user ""([^""]*)"" and black user ""([^""]*)"" with (\d+) moves? ""?([^""]*)""?")]
    public void GivenMatchIsFinishedWithMove(string matchId, string whiteId, string blackId, int moveCount, string move)
    {
        Match match = new()
        {
            Id = matchId,
            Status = MatchStatus.WhiteWon,
            White = new Player { UserId = whiteId },
            Black = new Player { UserId = blackId },
        };
        if (moveCount > 0)
        {
            match.Moves.Add(move);
        }

        context.SetupMatch(match);
    }

    [Given(@"match ""([^""]*)"" is finished with white user ""([^""]*)"" and black user ""([^""]*)"" with 0 moves")]
    public void GivenMatchIsFinishedWithNoMoves(string matchId, string whiteId, string blackId)
    {
        Match match = new()
        {
            Id = matchId,
            Status = MatchStatus.WhiteWon,
            White = new Player { UserId = whiteId },
            Black = new Player { UserId = blackId },
        };
        context.SetupMatch(match);
    }

    [Given(@"match ""([^""]*)"" is ongoing with white user ""([^""]*)"" and black user ""([^""]*)""")]
    public void GivenMatchIsOngoing(string matchId, string whiteId, string blackId)
    {
        Match match = new()
        {
            Id = matchId,
            Status = MatchStatus.Ongoing,
            White = new Player { UserId = whiteId },
            Black = new Player { UserId = blackId },
        };
        context.SetupMatch(match);
    }

    [Given(@"match ""([^""]*)"" does not exist")]
    public void GivenMatchDoesNotExist(string matchId) =>
        context.SetupMatchNotFound(matchId);

    [Given(@"match position (\d+) for ""([^""]*)"" is ""([^""]*)""")]
    public void GivenMatchPositionIs(int index, string matchId, string fen) =>
        context.SetupMatchPosition(matchId, index, fen);

    [Given(@"match ""([^""]*)"" has bot white ""([^""]*)"" and user black ""([^""]*)"" finished with black winning")]
    public void GivenMatchHasBotWhiteUserBlackFinishedWithBlackWinning(string matchId, string botId, string userId)
    {
        Match match = new()
        {
            Id = matchId,
            Status = MatchStatus.BlackWon,
            White = new Player { BotId = botId },
            Black = new Player { UserId = userId },
        };
        context.SetupMatch(match);
    }

    [Given(@"match ""([^""]*)"" has user white ""([^""]*)"" and no-identity black finished as draw")]
    public void GivenMatchHasUserWhiteNoIdentityBlackFinishedAsDraw(string matchId, string userId)
    {
        Match match = new()
        {
            Id = matchId,
            Status = MatchStatus.Draw,
            White = new Player { UserId = userId },
            Black = new Player(),
        };
        context.SetupMatch(match);
    }

    [Given(@"match ""([^""]*)"" has user white ""([^""]*)"" and bot black ""([^""]*)"" finished with white winning")]
    public void GivenMatchHasUserWhiteBotBlackFinishedWithWhiteWinning(string matchId, string userId, string botId)
    {
        Match match = new()
        {
            Id = matchId,
            Status = MatchStatus.WhiteWon,
            White = new Player { UserId = userId },
            Black = new Player { BotId = botId },
        };
        context.SetupMatch(match);
    }

    [Given(@"match ""([^""]*)"" has no-identity white and user black ""([^""]*)"" with unspecified status")]
    public void GivenMatchHasNoIdentityWhiteUserBlackUnspecifiedStatus(string matchId, string userId)
    {
        Match match = new()
        {
            Id = matchId,
            Status = MatchStatus.Unspecified,
            White = new Player(),
            Black = new Player { UserId = userId },
        };
        context.SetupMatch(match);
    }

    // ── When ─────────────────────────────────────────────────────────────────

    [When(@"""([^""]*)"" retrieves game ""([^""]*)""")]
    public async Task WhenRetrievesGame(string userId, string id)
    {
        context.LastException = null;
        try
        {
            context.LastGameResult = await context.Service.GetGameAsync(id, userId, CancellationToken.None);
        }
        catch (Exception ex)
        {
            context.LastException = ex;
        }
    }

    [When(@"""([^""]*)"" lists games page (\d+) page_size (\d+)")]
    public async Task WhenListsGames(string userId, int page, int pageSize)
    {
        context.LastException = null;
        try
        {
            context.LastListResult = await context.Service.ListGamesAsync(userId, page, pageSize, CancellationToken.None);
        }
        catch (Exception ex)
        {
            context.LastException = ex;
        }
    }

    [When(@"""([^""]*)"" deletes game ""([^""]*)""")]
    public async Task WhenDeletesGame(string userId, string id)
    {
        context.LastException = null;
        try
        {
            await context.Service.DeleteGameAsync(id, userId, CancellationToken.None);
        }
        catch (Exception ex)
        {
            context.LastException = ex;
        }
    }

    [When(@"""([^""]*)"" imports PGN ""([^""]*)""")]
    public async Task WhenImportsPgn(string userId, string pgn)
    {
        context.LastException = null;
        try
        {
            context.LastGameResult = await context.Service.ImportFromPgnAsync(pgn, userId, CancellationToken.None);
        }
        catch (Exception ex)
        {
            context.LastException = ex;
        }
    }

    [When(@"""([^""]*)"" imports the following PGN:")]
    public async Task WhenImportsFollowingPgn(string userId, string pgn)
    {
        context.LastException = null;
        try
        {
            context.LastGameResult = await context.Service.ImportFromPgnAsync(pgn, userId, CancellationToken.None);
        }
        catch (Exception ex)
        {
            context.LastException = ex;
        }
    }

    [When(@"""([^""]*)"" imports match ""([^""]*)""")]
    public async Task WhenImportsMatch(string userId, string matchId)
    {
        context.LastException = null;
        try
        {
            context.LastGameResult = await context.Service.ImportFromMatchAsync(matchId, userId, CancellationToken.None);
        }
        catch (Exception ex)
        {
            context.LastException = ex;
        }
    }

    // ── Then ─────────────────────────────────────────────────────────────────

    [Then(@"the result game has id ""([^""]*)""")]
    public void ThenResultGameHasId(string id) =>
        Assert.Equal(id, context.LastGameResult!.Id);

    [Then(@"the result game source is ""([^""]*)""")]
    public void ThenResultGameSourceIs(string source) =>
        Assert.Equal(source, context.LastGameResult!.Source);

    [Then(@"the result game has (\d+) moves?")]
    public void ThenResultGameHasMoves(int count) =>
        Assert.Equal(count, context.LastGameResult!.Moves.Count);

    [Then(@"the result game white name is ""([^""]*)""")]
    public void ThenResultGameWhiteNameIs(string name) =>
        Assert.Equal(name, context.LastGameResult!.White["name"]);

    [Then(@"the result game black name is ""([^""]*)""")]
    public void ThenResultGameBlackNameIs(string name) =>
        Assert.Equal(name, context.LastGameResult!.Black["name"]);

    [Then(@"the result game match id is ""([^""]*)""")]
    public void ThenResultGameMatchIdIs(string matchId) =>
        Assert.Equal(matchId, context.LastGameResult!.MatchId);

    [Then(@"the result game result is ""([^""]*)""")]
    public void ThenResultGameResultIs(string result) =>
        Assert.Equal(result, context.LastGameResult!.Result);

    [Then(@"the list result contains (\d+) games?")]
    public void ThenListResultContains(int count) =>
        Assert.Equal(count, context.LastListResult!.Value.Games.Count);

    [Then(@"the list result total is (\d+)")]
    public void ThenListResultTotalIs(int total) =>
        Assert.Equal(total, context.LastListResult!.Value.Total);

    [Then(@"the list result page is (\d+)")]
    public void ThenListResultPageIs(int page) =>
        Assert.Equal(page, context.LastListResult!.Value.Page);

    [Then(@"the list result page_size is (\d+)")]
    public void ThenListResultPageSizeIs(int pageSize) =>
        Assert.Equal(pageSize, context.LastListResult!.Value.PageSize);

    [Then(@"no exception is thrown")]
    public void ThenNoExceptionIsThrown() =>
        Assert.Null(context.LastException);

    [Then(@"an AnalysisGameNotFoundException is thrown")]
    public void ThenAnalysisGameNotFoundExceptionIsThrown() =>
        Assert.IsType<AnalysisGameNotFoundException>(context.LastException);

    [Then(@"an AccessDeniedException is thrown")]
    public void ThenAccessDeniedExceptionIsThrown() =>
        Assert.IsType<AccessDeniedException>(context.LastException);

    [Then(@"a MatchStillOngoingException is thrown")]
    public void ThenMatchStillOngoingExceptionIsThrown() =>
        Assert.IsType<MatchStillOngoingException>(context.LastException);

    [Then(@"a MatchAccessDeniedException is thrown")]
    public void ThenMatchAccessDeniedExceptionIsThrown() =>
        Assert.IsType<MatchAccessDeniedException>(context.LastException);

    [Then(@"an InvalidPgnException is thrown")]
    public void ThenInvalidPgnExceptionIsThrown() =>
        Assert.IsType<InvalidPgnException>(context.LastException);

    [Then(@"an InvalidPgnException is thrown with reason containing ""([^""]*)""")]
    public void ThenInvalidPgnExceptionIsThrownWithReason(string reason)
    {
        InvalidPgnException ex = Assert.IsType<InvalidPgnException>(context.LastException);
        Assert.Contains(reason, ex.Reason, StringComparison.Ordinal);
    }

    [Then(@"an RpcException with NotFound is thrown")]
    public void ThenRpcExceptionNotFoundIsThrown()
    {
        RpcException ex = Assert.IsType<RpcException>(context.LastException);
        Assert.Equal(StatusCode.NotFound, ex.StatusCode);
    }
}
