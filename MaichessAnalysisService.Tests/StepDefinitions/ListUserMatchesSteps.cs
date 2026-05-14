using System.Globalization;
using MaichessAnalysisService.Tests.Support;
using Reqnroll;
using Xunit;

namespace MaichessAnalysisService.Tests.StepDefinitions;

[Binding]
internal sealed class ListUserMatchesSteps(AnalysisServiceContext context)
{
    private readonly List<UserMatchFixture> whiteMatches = [];
    private readonly List<UserMatchFixture> blackMatches = [];
    private string? userId;

    [Given(@"user ""([^""]*)"" has the following finished matches as white:")]
    public void GivenUserHasFinishedMatchesAsWhite(string userIdArg, Table table)
    {
        userId = userIdArg;
        foreach (DataTableRow row in table.Rows)
        {
            whiteMatches.Add(BuildFixture(row, whiteUserId: userIdArg));
        }
    }

    [Given(@"user ""([^""]*)"" has the following finished matches as black:")]
    public void GivenUserHasFinishedMatchesAsBlack(string userIdArg, Table table)
    {
        userId = userIdArg;
        foreach (DataTableRow row in table.Rows)
        {
            blackMatches.Add(BuildFixture(row, blackUserId: userIdArg));
        }
    }

    [Given(@"user ""([^""]*)"" has no finished matches as white")]
    public void GivenUserHasNoFinishedMatchesAsWhite(string userIdArg) => userId = userIdArg;

    [Given(@"user ""([^""]*)"" has no finished matches as black")]
    public void GivenUserHasNoFinishedMatchesAsBlack(string userIdArg) => userId = userIdArg;

    [Given(@"user ""([^""]*)"" has (\d+) finished matches as white starting ""([^""]*)""")]
    public void GivenUserHasManyFinishedMatchesAsWhite(string userIdArg, int count, string startIso)
    {
        userId = userIdArg;
        DateTimeOffset start = DateTimeOffset.Parse(startIso, CultureInfo.InvariantCulture);
        for (int i = 0; i < count; i++)
        {
            whiteMatches.Add(new UserMatchFixture(
                Id: $"match-{i + 1}",
                Status: "white_won",
                WhiteUserId: userIdArg,
                BlackUserId: "opponent",
                WhiteBotId: null,
                BlackBotId: null,
                TimeFormatId: "5+0",
                BaseMs: 300_000,
                IncrementMs: 0,
                Category: "blitz",
                LastMoveAt: start.AddMinutes(i),
                Moves: ["e2e4"]));
        }
    }

    [When(@"""([^""]*)"" lists their finished matches page (\d+) page_size (\d+)")]
    public async Task WhenListsFinishedMatches(string userIdArg, int page, int pageSize)
    {
        context.SetupUserMatches(userIdArg, whiteMatches, blackMatches);
        context.LastUserMatchesResult = await context.Service.ListUserMatchesAsync(
            userIdArg, page, pageSize, CancellationToken.None);
    }

    [Then(@"the user matches result contains (\d+) matches")]
    public void ThenResultContainsMatches(int expected) =>
        Assert.Equal(expected, context.LastUserMatchesResult!.Value.Matches.Count);

    [Then(@"the user matches result total is (\d+)")]
    public void ThenResultTotalIs(int expected) =>
        Assert.Equal(expected, context.LastUserMatchesResult!.Value.Total);

    [Then(@"the user matches result page_size is (\d+)")]
    public void ThenResultPageSizeIs(int expected) =>
        Assert.Equal(expected, context.LastUserMatchesResult!.Value.PageSize);

    [Then(@"the user matches result first match id is ""([^""]*)""")]
    public void ThenFirstMatchIdIs(string expected) =>
        Assert.Equal(expected, context.LastUserMatchesResult!.Value.Matches[0].MatchId);

    [Then(@"the user matches first time format id is ""([^""]*)""")]
    public void ThenFirstTimeFormatIdIs(string expected) =>
        Assert.Equal(expected, context.LastUserMatchesResult!.Value.Matches[0].TimeFormat.Id);

    [Then(@"the user matches first increment ms is (\d+)")]
    public void ThenFirstIncrementMsIs(long expected) =>
        Assert.Equal(expected, context.LastUserMatchesResult!.Value.Matches[0].TimeFormat.IncrementMs);

    private static UserMatchFixture BuildFixture(
        DataTableRow row,
        string? whiteUserId = null,
        string? blackUserId = null) =>
        new(
            Id: row["id"],
            Status: row["status"],
            WhiteUserId: whiteUserId,
            BlackUserId: blackUserId,
            WhiteBotId: null,
            BlackBotId: null,
            TimeFormatId: row["time_format_id"],
            BaseMs: row["time_format_id"] switch
            {
                "1+0" => 60_000,
                "3+0" => 180_000,
                "3+2" => 180_000,
                "5+0" => 300_000,
                "10+0" => 600_000,
                _ => 300_000,
            },
            IncrementMs: row["time_format_id"] switch
            {
                "3+2" => 2_000,
                "5+3" => 3_000,
                "10+5" => 5_000,
                _ => 0,
            },
            Category: "blitz",
            LastMoveAt: DateTimeOffset.Parse(row["last_move_at"], CultureInfo.InvariantCulture),
            Moves: ["e2e4"]);
}
