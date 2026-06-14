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

    [Given(@"the move validator accepts SAN ""([^""]*)"" at the initial FEN as UCI ""([^""]*)"" resulting in ""([^""]*)""")]
    public void GivenMoveValidatorAcceptsSanAtInitialFen(string san, string uci, string resultingFen) =>
        context.SetupValidateMoveSan(InitialFen, san, uci, resultingFen);

    [Given(@"the move validator accepts SAN ""([^""]*)"" at ""([^""]*)"" as UCI ""([^""]*)"" resulting in ""([^""]*)""")]
    public void GivenMoveValidatorAcceptsSanAtFen(string san, string fen, string uci, string resultingFen) =>
        context.SetupValidateMoveSan(fen, san, uci, resultingFen);

    [Given(@"the move validator rejects SAN ""([^""]*)"" at the initial FEN")]
    public void GivenMoveValidatorRejectsSan(string san) =>
        context.SetupValidateMoveSanInvalid(san, $"illegal or unrecognised move: {san}");

    [Given(@"match ""([^""]*)"" exists with status ""([^""]*)"" and white user ""([^""]*)"" and black user ""([^""]*)""")]
    public void GivenMatchWithWhiteAndBlackUser(string matchId, string status, string whiteId, string blackId) =>
        context.SetupMatch(matchId, status, whiteId, blackId, null, null, [], [InitialFen]);

    [Given(@"match ""([^""]*)"" has moves ""([^""]*)"" and san ""([^""]*)"" from the initial fen")]
    public void GivenMatchHasMoveAndSan(string matchId, string uciMove, string sanMove)
    {
        string afterMoveFen = "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1";

        // Rebuild with moves
        context.SetupMatch(matchId, "white_won", "user-1", "user-2", null, null,
            [uciMove], [InitialFen, afterMoveFen]);
        context.SetupConvertSequenceToSan(InitialFen, [uciMove], [sanMove]);
    }

    [Given(@"match ""([^""]*)"" exists with status ""([^""]*)"" and white bot ""([^""]*)"" and black user ""([^""]*)""")]
    public void GivenMatchWithBotWhiteUserBlack(string matchId, string status, string botId, string userId) =>
        context.SetupMatch(matchId, status, null, userId, botId, null, [], [InitialFen]);

    [Given(@"match ""([^""]*)"" exists with status ""([^""]*)"" and white user ""([^""]*)"" and black bot ""([^""]*)""")]
    public void GivenMatchWithUserWhiteBotBlack(string matchId, string status, string userId, string botId) =>
        context.SetupMatch(matchId, status, userId, null, null, botId, [], [InitialFen]);

    [Given(@"match ""([^""]*)"" exists with status ""([^""]*)"" and white user ""([^""]*)"" and no black")]
    public void GivenMatchWithUserWhiteNoBlack(string matchId, string status, string userId) =>
        context.SetupMatch(matchId, status, userId, null, null, null, [], [InitialFen]);

    [Given(@"match ""([^""]*)"" exists with status ""([^""]*)"" and no white and black user ""([^""]*)""")]
    public void GivenMatchWithNoWhiteUserBlack(string matchId, string status, string userId) =>
        context.SetupMatch(matchId, status, null, userId, null, null, [], [InitialFen]);

    [Given(@"match ""([^""]*)"" exists with status ""([^""]*)"" and white bot ""([^""]*)"" and black bot ""([^""]*)"" created by ""([^""]*)""")]
    public void GivenBotVsBotMatchCreatedBy(string matchId, string status, string whiteBot, string blackBot, string creatorId) =>
        context.SetupMatch(matchId, status, null, null, whiteBot, blackBot, [], [InitialFen], creatorId);

    [Given(@"match ""([^""]*)"" does not exist")]
    public void GivenMatchDoesNotExist(string matchId) =>
        context.SetupMatchNotFound(matchId);

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

    [When(@"""([^""]*)"" imports FEN ""([^""]*)""")]
    public async Task WhenImportsFen(string userId, string fen)
    {
        context.LastException = null;
        try
        {
            context.LastGameResult = await context.Service.ImportFromFenAsync(fen, userId, CancellationToken.None);
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

    [Then(@"the result game starting fen is ""([^""]*)""")]
    public void ThenResultGameStartingFenIs(string fen) =>
        Assert.Equal(fen, context.LastGameResult!.StartingFen);

    [Then(@"the result game has no moves and no fens")]
    public void ThenResultGameHasNoMovesAndNoFens()
    {
        Assert.Empty(context.LastGameResult!.Moves);
        Assert.Empty(context.LastGameResult!.Fens);
    }

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
}
