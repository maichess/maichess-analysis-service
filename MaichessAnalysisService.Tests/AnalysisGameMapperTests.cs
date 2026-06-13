using System.Globalization;
using MaichessAnalysisService.Domain;
using MaichessAnalysisService.Rest;
using MaichessAnalysisService.Tests.Support;
using Xunit;

namespace MaichessAnalysisService.Tests;

public sealed class AnalysisGameMapperTests
{
    [Fact]
    public void ToSummary_MapsAllFieldsCorrectly()
    {
        AnalysisGame game = AnalysisServiceContext.BuildGame("game-42", "user-7");

        GameSummaryResponse summary = AnalysisGameMapper.ToSummary(game);

        Assert.Equal("game-42", summary.Id);
        Assert.Equal("pgn", summary.Source);
        Assert.Null(summary.MatchId);
        Assert.Equal(game.White, summary.White);
        Assert.Equal(game.Black, summary.Black);
        Assert.Equal("*", summary.Result);
        Assert.Equal(2, summary.MoveCount);
        Assert.Equal(game.CreatedAt.ToString("O", CultureInfo.InvariantCulture), summary.CreatedAt);
        Assert.Equal(game.Tags, summary.Tags);
    }

    [Fact]
    public void ToSummary_WithMatchId_PreservesMatchId()
    {
        AnalysisGame game = AnalysisServiceContext.BuildGame() with { MatchId = "match-99" };

        GameSummaryResponse summary = AnalysisGameMapper.ToSummary(game);

        Assert.Equal("match-99", summary.MatchId);
    }

    [Fact]
    public void ToDetail_MapsAllFieldsIncludingMovesAndFens()
    {
        AnalysisGame game = AnalysisServiceContext.BuildGame("game-42", "user-7");

        GameDetailResponse detail = AnalysisGameMapper.ToDetail(game);

        Assert.Equal("game-42", detail.Id);
        Assert.Equal("pgn", detail.Source);
        Assert.Equal(game.StartingFen, detail.StartingFen);
        Assert.Null(detail.MatchId);
        Assert.Equal(game.White, detail.White);
        Assert.Equal(game.Black, detail.Black);
        Assert.Equal("*", detail.Result);
        Assert.Equal(2, detail.MoveCount);
        Assert.Equal(game.CreatedAt.ToString("O", CultureInfo.InvariantCulture), detail.CreatedAt);
        Assert.Equal(game.Tags, detail.Tags);
        Assert.Equal(game.Moves, detail.Moves);
        Assert.Equal(game.Fens, detail.Fens);
        Assert.Equal(game.Pgn, detail.Pgn);
    }

    [Fact]
    public void ToDetail_WithMatchId_PreservesMatchId()
    {
        AnalysisGame game = AnalysisServiceContext.BuildGame() with { MatchId = "match-77" };

        GameDetailResponse detail = AnalysisGameMapper.ToDetail(game);

        Assert.Equal("match-77", detail.MatchId);
    }

    [Fact]
    public void ToUserMatchSummary_MapsFieldsAndNestedTimeFormat()
    {
        UserMatchSummary match = new(
            MatchId: "match-5",
            White: new Dictionary<string, string> { ["username"] = "alice" },
            Black: new Dictionary<string, string> { ["bot_id"] = "stockfish-3" },
            Status: "white_won",
            TimeFormat: new UserMatchTimeFormat("5+0", 300000, 0, "blitz"),
            MoveCount: 42,
            FinishedAtMs: 1_700_000_000_000);

        UserMatchSummaryResponse response = AnalysisGameMapper.ToUserMatchSummary(match);

        Assert.Equal("match-5", response.MatchId);
        Assert.Equal(match.White, response.White);
        Assert.Equal(match.Black, response.Black);
        Assert.Equal("white_won", response.Status);
        Assert.Equal(42, response.MoveCount);
        Assert.Equal(1_700_000_000_000, response.FinishedAtMs);
        Assert.Equal("5+0", response.TimeFormat.Id);
        Assert.Equal(300000, response.TimeFormat.BaseMs);
        Assert.Equal(0, response.TimeFormat.IncrementMs);
        Assert.Equal("blitz", response.TimeFormat.Category);
    }
}
