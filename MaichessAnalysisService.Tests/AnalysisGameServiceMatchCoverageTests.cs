using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Maichess.Database.V1;
using Maichess.Engine.V1;
using Maichess.MoveValidator.V1;
using Maichess.User.V1;
using MaichessAnalysisService.Domain;
using MaichessAnalysisService.Tests.Support;
using NSubstitute;
using Xunit;

namespace MaichessAnalysisService.Tests;

// Branch coverage for the match-import and finished-match listing paths:
// player resolution (user/bot/neither, RPC failures), PGN name fallbacks, the
// legacy time-control mapping and the dedup/skip rules.
public sealed class AnalysisGameServiceMatchCoverageTests
{
    private const string InitialFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    private readonly AnalysisServiceContext ctx = new();

    private static Value StrList(params string[] v) => Value.ForList([.. v.Select(Value.ForString)]);

    private void SetupMatchRaw(string matchId, Struct record) =>
        ctx.DbClient
            .GetAsync(
                Arg.Is<GetRequest>(r => r.Collection == "matches" && r.Id == matchId),
                Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(GrpcHelper.GrpcCall(new GetResponse { Record = record }));

    private void SetupList(string field, string userId, params Struct[] records)
    {
        ListResponse resp = new();
        resp.Records.AddRange(records);
        ctx.DbClient
            .ListAsync(
                Arg.Is<ListRequest>(r =>
                    r.Collection == "matches" &&
                    r.Filter.Fields.ContainsKey(field) &&
                    r.Filter.Fields[field].StringValue == userId),
                Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(GrpcHelper.GrpcCall(resp));
    }

    private void GetUserThrows(string userId) =>
        ctx.UsersClient
            .GetUserAsync(
                Arg.Is<GetUserRequest>(r => r.UserId == userId),
                Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns<AsyncUnaryCall<GetUserResponse>>(_ =>
                throw new RpcException(new Status(StatusCode.Unavailable, "down")));

    private void ListBotsThrows() =>
        ctx.BotsClient
            .ListBotsAsync(
                Arg.Any<ListBotsRequest>(),
                Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns<AsyncUnaryCall<ListBotsResponse>>(_ =>
                throw new RpcException(new Status(StatusCode.Unavailable, "down")));

    // ── ImportFromMatch: player resolution + PGN name fallbacks ──────────────

    [Fact]
    public async Task Import_ResolvesUserUsername_AndBotName()
    {
        Struct match = new();
        match.Fields["status"] = Value.ForString("white_won");
        match.Fields["white_user_id"] = Value.ForString("wu");
        match.Fields["black_bot_id"] = Value.ForString("stockfish-3");
        match.Fields["moves"] = StrList("e2e4", "e7e5");
        match.Fields["fen_history"] = StrList("fen0", "fen1", "fen2");
        SetupMatchRaw("m-1", match);
        ctx.SetupUserResolution("wu", "alice");
        ConvertSequenceToSanResponse san = new();
        san.SanMoves.AddRange(["e4", "e5"]);
        ctx.MovesClient
            .ConvertSequenceToSanAsync(
                Arg.Any<ConvertSequenceToSanRequest>(),
                Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(GrpcHelper.GrpcCall(san));

        AnalysisGame game = await ctx.Service.ImportFromMatchAsync("m-1", "wu", CancellationToken.None);

        Assert.Equal("alice", game.White["username"]);
        Assert.Equal("Stockfish Level 3", game.Black["name"]);
        Assert.Equal("1-0", game.Result);
        Assert.Equal("fen0", game.StartingFen);
        Assert.Contains("[White \"alice\"]", game.Pgn, StringComparison.Ordinal);
        Assert.Contains("[Black \"Stockfish Level 3\"]", game.Pgn, StringComparison.Ordinal);
        Assert.Contains("1. e4 e5", game.Pgn, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Import_UserRpcFails_FallsBackToUserId_AndUnknownBotFallsBackToBotId()
    {
        Struct match = new();
        match.Fields["status"] = Value.ForString("black_won");
        match.Fields["white_user_id"] = Value.ForString("wu");
        match.Fields["black_bot_id"] = Value.ForString("bot-unknown");
        match.Fields["fen_history"] = StrList("fen0");
        SetupMatchRaw("m-2", match);
        GetUserThrows("wu");

        AnalysisGame game = await ctx.Service.ImportFromMatchAsync("m-2", "wu", CancellationToken.None);

        Assert.Equal("wu", game.White["user_id"]);
        Assert.False(game.White.ContainsKey("username"));
        Assert.Equal("bot-unknown", game.Black["bot_id"]);
        Assert.False(game.Black.ContainsKey("name"));
        Assert.Equal("0-1", game.Result);
        Assert.Contains("[White \"wu\"]", game.Pgn, StringComparison.Ordinal);
        Assert.Contains("[Black \"bot-unknown\"]", game.Pgn, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Import_BotRpcFails_FallsBackToBotId_AndEmptyPlayerBecomesQuestionMark()
    {
        Struct match = new();
        match.Fields["status"] = Value.ForString("draw");
        match.Fields["white_bot_id"] = Value.ForString("bot-y");
        // black has neither user nor bot → empty info → "?" in the PGN.
        // The importer is the match's creator (a bot-vs-bot game they spawned).
        match.Fields["created_by_user_id"] = Value.ForString("me");
        match.Fields["fen_history"] = StrList("fen0");
        SetupMatchRaw("m-3", match);
        ListBotsThrows();

        AnalysisGame game = await ctx.Service.ImportFromMatchAsync("m-3", "me", CancellationToken.None);

        Assert.Equal("bot-y", game.White["bot_id"]);
        Assert.Empty(game.Black);
        Assert.Equal("1/2-1/2", game.Result);
        Assert.Contains("[Black \"?\"]", game.Pgn, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Import_MissingStatus_DefaultsResult_AndEmptyHistoryUsesInitialFen()
    {
        // No status, no fen_history, no moves: exercises the status/history/list defaults.
        Struct match = new();
        match.Fields["white_user_id"] = Value.ForString("me");
        SetupMatchRaw("m-4", match);
        ctx.SetupUserResolution("me", "me-name");

        AnalysisGame game = await ctx.Service.ImportFromMatchAsync("m-4", "me", CancellationToken.None);

        Assert.Equal("*", game.Result);
        Assert.Equal(InitialFen, game.StartingFen);
        Assert.Empty(game.Moves);
        Assert.Empty(game.Fens);
    }

    [Fact]
    public async Task Import_WhiteBotName_AndBlackUserUsername()
    {
        Struct match = new();
        match.Fields["status"] = Value.ForString("white_won");
        match.Fields["white_bot_id"] = Value.ForString("stockfish-3");
        match.Fields["black_user_id"] = Value.ForString("bu");
        match.Fields["fen_history"] = StrList("fen0");
        SetupMatchRaw("m-5", match);
        ctx.SetupUserResolution("bu", "bob");

        AnalysisGame game = await ctx.Service.ImportFromMatchAsync("m-5", "bu", CancellationToken.None);

        Assert.Equal("Stockfish Level 3", game.White["name"]);
        Assert.Equal("bob", game.Black["username"]);
        Assert.Contains("[White \"Stockfish Level 3\"]", game.Pgn, StringComparison.Ordinal);
        Assert.Contains("[Black \"bob\"]", game.Pgn, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Import_EmptyWhiteBecomesQuestionMark_AndBlackUserIdFallback()
    {
        Struct match = new();
        match.Fields["status"] = Value.ForString("white_won");
        // white has neither user nor bot → "?"; black is a user whose lookup fails.
        match.Fields["black_user_id"] = Value.ForString("bu2");
        match.Fields["fen_history"] = StrList("fen0");
        SetupMatchRaw("m-6", match);
        GetUserThrows("bu2");

        AnalysisGame game = await ctx.Service.ImportFromMatchAsync("m-6", "bu2", CancellationToken.None);

        Assert.Empty(game.White);
        Assert.Equal("bu2", game.Black["user_id"]);
        Assert.False(game.Black.ContainsKey("username"));
        Assert.Contains("[White \"?\"]", game.Pgn, StringComparison.Ordinal);
        Assert.Contains("[Black \"bu2\"]", game.Pgn, StringComparison.Ordinal);
    }

    // ── ListUserMatches: dedup / skip rules ──────────────────────────────────

    [Fact]
    public async Task List_SkipsNullId_Ongoing_EmptyStatus_AndDuplicates()
    {
        Struct noId = new();
        noId.Fields["status"] = Value.ForString("white_won");

        Struct good = Finished("keep", "white_won");

        Struct ongoing = new();
        ongoing.Fields["id"] = Value.ForString("ong");
        ongoing.Fields["status"] = Value.ForString("ongoing");

        Struct noStatus = new();
        noStatus.Fields["id"] = Value.ForString("nostatus");

        SetupList("white_user_id", "u", noId, good, ongoing, noStatus);
        // A duplicate of "keep" arriving from the black side must be ignored.
        SetupList("black_user_id", "u", Finished("keep", "black_won"));

        (IReadOnlyList<UserMatchSummary> Matches, long Total, int Page, int PageSize) result =
            await ctx.Service.ListUserMatchesAsync("u", 1, 20, CancellationToken.None);

        UserMatchSummary only = Assert.Single(result.Matches);
        Assert.Equal("keep", only.MatchId);
        Assert.Equal("white_won", only.Status);
    }

    // ── ReadTimeFormat / ParseLastMoveAtMs / GetStringList ───────────────────

    [Theory]
    [InlineData("bullet", "3+0", 180000L)]
    [InlineData("blitz", "5+0", 300000L)]
    [InlineData("rapid", "10+0", 600000L)]
    [InlineData("classical", "30+0", 1800000L)]
    [InlineData("weird", "5+0", 300000L)]
    public async Task List_LegacyTimeControl_MapsToPresetFormat(string legacy, string expectedId, long expectedBase)
    {
        Struct m = new();
        m.Fields["id"] = Value.ForString("m");
        m.Fields["status"] = Value.ForString("white_won");
        m.Fields["time_control"] = Value.ForString(legacy);
        m.Fields["last_move_at"] = Value.ForString("2026-05-01T10:00:00Z");
        SetupList("white_user_id", "u", m);
        SetupList("black_user_id", "u");

        (IReadOnlyList<UserMatchSummary> Matches, _, _, _) =
            await ctx.Service.ListUserMatchesAsync("u", 1, 20, CancellationToken.None);

        UserMatchSummary summary = Assert.Single(Matches);
        Assert.Equal(expectedId, summary.TimeFormat.Id);
        Assert.Equal(expectedBase, summary.TimeFormat.BaseMs);
        Assert.Equal(legacy, summary.TimeFormat.Category);
    }

    [Fact]
    public async Task List_NoTimeControl_DefaultsToBlitz_AndMissingOrBadLastMoveIsZero()
    {
        // No time_control at all → legacy fallback default ("blitz"); no last_move_at
        // and no moves list → ParseLastMoveAtMs 0 and GetStringList empty.
        Struct noTc = new();
        noTc.Fields["id"] = Value.ForString("a");
        noTc.Fields["status"] = Value.ForString("white_won");

        // Unparseable timestamp also yields 0.
        Struct badTs = new();
        badTs.Fields["id"] = Value.ForString("b");
        badTs.Fields["status"] = Value.ForString("white_won");
        badTs.Fields["last_move_at"] = Value.ForString("not-a-date");
        badTs.Fields["moves"] = StrList("e2e4");

        SetupList("white_user_id", "u", noTc, badTs);
        SetupList("black_user_id", "u");

        (IReadOnlyList<UserMatchSummary> Matches, _, _, _) =
            await ctx.Service.ListUserMatchesAsync("u", 1, 20, CancellationToken.None);

        Assert.Equal(2, Matches.Count);
        Assert.All(Matches, s => Assert.Equal(0, s.FinishedAtMs));
        UserMatchSummary a = Matches.Single(s => s.MatchId == "a");
        Assert.Equal("5+0", a.TimeFormat.Id);
        Assert.Equal("blitz", a.TimeFormat.Category);
        Assert.Equal(0, a.MoveCount);
        Assert.Equal(1, Matches.Single(s => s.MatchId == "b").MoveCount);
    }

    [Fact]
    public async Task List_TimeFormatId_PreservesEmbeddedFormat()
    {
        Struct m = new();
        m.Fields["id"] = Value.ForString("m");
        m.Fields["status"] = Value.ForString("white_won");
        m.Fields["time_format_id"] = Value.ForString("3+2");
        m.Fields["time_format_base_ms"] = Value.ForNumber(180000);
        m.Fields["time_format_increment_ms"] = Value.ForNumber(2000);
        // time_format_category intentionally omitted → defaults to empty.
        m.Fields["last_move_at"] = Value.ForString("2026-05-01T10:00:00Z");
        SetupList("white_user_id", "u", m);
        SetupList("black_user_id", "u");

        (IReadOnlyList<UserMatchSummary> Matches, _, _, _) =
            await ctx.Service.ListUserMatchesAsync("u", 1, 20, CancellationToken.None);

        UserMatchSummary summary = Assert.Single(Matches);
        Assert.Equal("3+2", summary.TimeFormat.Id);
        Assert.Equal(2000, summary.TimeFormat.IncrementMs);
        Assert.Equal(string.Empty, summary.TimeFormat.Category);
        Assert.True(summary.FinishedAtMs > 0);
    }

    private static Struct Finished(string id, string status)
    {
        Struct s = new();
        s.Fields["id"] = Value.ForString(id);
        s.Fields["status"] = Value.ForString(status);
        s.Fields["time_format_id"] = Value.ForString("5+0");
        s.Fields["last_move_at"] = Value.ForString("2026-05-01T10:00:00Z");
        return s;
    }
}
