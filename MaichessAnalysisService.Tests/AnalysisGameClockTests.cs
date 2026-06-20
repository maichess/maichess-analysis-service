using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Maichess.Database.V1;
using Maichess.MoveValidator.V1;
using MaichessAnalysisService.Domain;
using MaichessAnalysisService.Services;
using MaichessAnalysisService.Tests.Support;
using NSubstitute;
using Xunit;

namespace MaichessAnalysisService.Tests;

// Covers per-move clock annotations end-to-end in the analysis service: the pure
// parse/format helpers, clock-history carried off a match document into the exported
// PGN + AnalysisGame, and clock comments parsed back out of an imported PGN.
public sealed class AnalysisGameClockTests
{
    private const string InitialFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    private readonly AnalysisServiceContext ctx = new();

    // ── FormatPgnClock ───────────────────────────────────────────────────────

    [Theory]
    [InlineData(0L, "0:00:00")]
    [InlineData(1_000L, "0:00:01")]
    [InlineData(59_000L, "0:00:59")]
    [InlineData(60_000L, "0:01:00")]
    [InlineData(299_000L, "0:04:59")]
    [InlineData(3_600_000L, "1:00:00")]
    [InlineData(3_661_000L, "1:01:01")]
    [InlineData(-5_000L, "0:00:00")]
    [InlineData(1_999L, "0:00:01")]
    public void FormatPgnClock_RendersHMmSs(long ms, string expected) =>
        Assert.Equal(expected, AnalysisGameService.FormatPgnClock(ms));

    // ── ParseClockMs ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("0:02:59", 179_000L)]
    [InlineData("2:59", 179_000L)]
    [InlineData("59", 59_000L)]
    [InlineData("1:00:00", 3_600_000L)]
    [InlineData("0:00:01.5", 1_500L)]
    public void ParseClockMs_ParsesClockTokens(string token, long expected) =>
        Assert.Equal(expected, AnalysisGameService.ParseClockMs(token));

    [Theory]
    [InlineData("")]
    [InlineData("1:2:3:4")]
    [InlineData("abc")]
    [InlineData("1:xx")]
    [InlineData("-5")]
    public void ParseClockMs_RejectsBadTokens(string token) =>
        Assert.Null(AnalysisGameService.ParseClockMs(token));

    // ── ParseMoveClocks ──────────────────────────────────────────────────────

    [Fact]
    public void ParseMoveClocks_AlignsClkCommentsToPlies()
    {
        const string pgn = "[White \"A\"]\n[Black \"B\"]\n\n" +
            "1. e4 {[%clk 0:03:00]} e5 {[%clk 0:02:55]} 2. Nf3 {[%clk 0:02:58]} 1-0";

        IReadOnlyList<long?> clocks = AnalysisGameService.ParseMoveClocks(pgn);

        Assert.Equal(new long?[] { 180_000, 175_000, 178_000 }, clocks);
    }

    [Fact]
    public void ParseMoveClocks_ParsesEmtComments()
    {
        IReadOnlyList<long?> clocks = AnalysisGameService.ParseMoveClocks("[W \"a\"]\n\n1. e4 {[%emt 0:00:05]} 1-0");

        Assert.Equal(new long?[] { 5_000 }, clocks);
    }

    [Fact]
    public void ParseMoveClocks_NoClockComments_AllNull()
    {
        IReadOnlyList<long?> clocks = AnalysisGameService.ParseMoveClocks("[W \"a\"]\n\n1. e4 e5 2. Nf3 *");

        Assert.Equal(new long?[] { null, null, null }, clocks);
    }

    [Fact]
    public void ParseMoveClocks_NonClockComment_IsIgnored()
    {
        IReadOnlyList<long?> clocks = AnalysisGameService.ParseMoveClocks("[W \"a\"]\n\n1. e4 {a great move} e5 *");

        Assert.Equal(new long?[] { null, null }, clocks);
    }

    [Fact]
    public void ParseMoveClocks_CommentBeforeAnyMove_IsIgnored()
    {
        IReadOnlyList<long?> clocks = AnalysisGameService.ParseMoveClocks("[W \"a\"]\n\n{[%clk 0:05:00]} 1. e4 *");

        Assert.Equal(new long?[] { null }, clocks);
    }

    [Fact]
    public void ParseMoveClocks_SecondCommentDoesNotOverwriteFirst()
    {
        IReadOnlyList<long?> clocks =
            AnalysisGameService.ParseMoveClocks("[W \"a\"]\n\n1. e4 {[%clk 0:03:00]} {[%clk 0:02:00]} e5 *");

        Assert.Equal(new long?[] { 180_000, null }, clocks);
    }

    [Fact]
    public void ParseMoveClocks_UnterminatedComment_StopsParsing()
    {
        IReadOnlyList<long?> clocks = AnalysisGameService.ParseMoveClocks("[W \"a\"]\n\n1. e4 {[%clk 0:03:00 e5 *");

        Assert.Equal(new long?[] { null }, clocks);
    }

    [Fact]
    public void ParseMoveClocks_NoTags_UsesWholeStringAsMovetext()
    {
        IReadOnlyList<long?> clocks = AnalysisGameService.ParseMoveClocks("1. e4 {[%clk 0:03:00]} *");

        Assert.Equal(new long?[] { 180_000 }, clocks);
    }

    [Fact]
    public void ParseMoveClocks_MovetextEndingExactlyOnAMove_DoesNotOverrun()
    {
        // A move token at the very end of the movetext (no trailing result/whitespace)
        // must stop exactly at the string end rather than read past it.
        IReadOnlyList<long?> clocks = AnalysisGameService.ParseMoveClocks("1. e4");

        Assert.Equal(new long?[] { null }, clocks);
    }

    // ── BuildClockHistory ────────────────────────────────────────────────────

    [Fact]
    public void BuildClockHistory_CarriesOpponentClockForward()
    {
        IReadOnlyList<ClockSnapshot> history =
            AnalysisGameService.BuildClockHistory(4, [180_000, 175_000, 178_000, 170_000]);

        Assert.Equal(
            new[]
            {
                new ClockSnapshot(180_000, 0),
                new ClockSnapshot(180_000, 175_000),
                new ClockSnapshot(178_000, 175_000),
                new ClockSnapshot(178_000, 170_000),
            },
            history);
    }

    [Fact]
    public void BuildClockHistory_NoClocks_ReturnsEmpty() =>
        Assert.Empty(AnalysisGameService.BuildClockHistory(2, [null, null]));

    [Fact]
    public void BuildClockHistory_ClockBeyondMoveCount_IsNotConsidered()
    {
        // The lone clock sits past moveCount, so it must not count as "has clock data".
        Assert.Empty(AnalysisGameService.BuildClockHistory(1, [null, 180_000]));
    }

    [Fact]
    public void BuildClockHistory_FewerClocksThanMoves_ScansOnlyWithinBounds()
    {
        // moveCount exceeds the clock list; scanning must stay within plyClocks.
        Assert.Empty(AnalysisGameService.BuildClockHistory(3, [null]));
    }

    [Fact]
    public void BuildClockHistory_PartialClocks_FillsKnownPliesOnly()
    {
        // Only black's first move (ply 1) carries a clock; white stays at its 0 seed.
        IReadOnlyList<ClockSnapshot> history = AnalysisGameService.BuildClockHistory(3, [null, 175_000]);

        Assert.Equal(
            new[]
            {
                new ClockSnapshot(0, 0),
                new ClockSnapshot(0, 175_000),
                new ClockSnapshot(0, 175_000),
            },
            history);
    }

    // ── ExtractMovetext ──────────────────────────────────────────────────────

    [Fact]
    public void ExtractMovetext_NoTags_ReturnsWholeString() =>
        Assert.Equal("1. e4 *", AnalysisGameService.ExtractMovetext("1. e4 *"));

    [Fact]
    public void ExtractMovetext_WithClockComments_KeepsFullMoveList()
    {
        const string pgn = "[White \"A\"]\n\n1. e4 {[%clk 0:03:00]} e5 {[%clk 0:02:55]} *";

        string movetext = AnalysisGameService.ExtractMovetext(pgn);

        Assert.Contains("1. e4", movetext, StringComparison.Ordinal);
        Assert.Contains("e5", movetext, StringComparison.Ordinal);
    }

    [Fact]
    public void ExtractMovetext_MultipleTags_StripsBothTagsAndReturnsMovetext()
    {
        // Two tags: last.Index is non-zero, so the correct result requires using
        // last.Index + last.Length, not just last.Length.
        const string pgn = "[White \"A\"]\n[Black \"B\"]\n\n1. e4 *";

        string movetext = AnalysisGameService.ExtractMovetext(pgn);

        Assert.DoesNotContain("Black", movetext, StringComparison.Ordinal);
        Assert.Contains("1. e4", movetext, StringComparison.Ordinal);
    }

    // ── ImportFromMatch: clock_history → PGN + AnalysisGame ──────────────────

    [Fact]
    public async Task ImportFromMatch_WithClockHistory_EmitsClkCommentsAndCarriesSnapshots()
    {
        Struct match = new();
        match.Fields["status"] = Value.ForString("white_won");
        match.Fields["white_user_id"] = Value.ForString("wu");
        match.Fields["black_user_id"] = Value.ForString("bu");
        match.Fields["moves"] = Value.ForList([Value.ForString("e2e4"), Value.ForString("e7e5")]);
        match.Fields["fen_history"] = Value.ForList(
            [Value.ForString(InitialFen), Value.ForString("fen1"), Value.ForString("fen2")]);
        match.Fields["clock_history"] = Value.ForList([Clock(299_000, 300_000), Clock(299_000, 298_000)]);
        SetupMatchRaw("m-clk", match);
        ctx.SetupUserResolution("wu", "alice");
        ctx.SetupUserResolution("bu", "bob");
        ctx.SetupConvertSequenceToSan(InitialFen, ["e2e4", "e7e5"], ["e4", "e5"]);

        AnalysisGame game = await ctx.Service.ImportFromMatchAsync("m-clk", "wu", CancellationToken.None);

        Assert.Equal(
            new[] { new ClockSnapshot(299_000, 300_000), new ClockSnapshot(299_000, 298_000) },
            game.ClockHistory);
        Assert.Contains("1. e4 {[%clk 0:04:59]} e5 {[%clk 0:04:58]}", game.Pgn, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ImportFromMatch_ThreeMoves_UsesMoverClockAndMoveNumberPerPly()
    {
        // Three plies exercise the second move number ("2.") and a white move at an
        // even ply beyond the first, pinning the i%2 mover-side and (i/2)+1 numbering.
        Struct match = new();
        match.Fields["status"] = Value.ForString("white_won");
        match.Fields["white_user_id"] = Value.ForString("wu");
        match.Fields["black_user_id"] = Value.ForString("bu");
        match.Fields["moves"] = Value.ForList(
            [Value.ForString("e2e4"), Value.ForString("e7e5"), Value.ForString("g1f3")]);
        match.Fields["fen_history"] = Value.ForList(
            [Value.ForString(InitialFen), Value.ForString("f1"), Value.ForString("f2"), Value.ForString("f3")]);
        match.Fields["clock_history"] = Value.ForList(
            [Clock(299_000, 300_000), Clock(299_000, 298_000), Clock(297_000, 298_000)]);
        SetupMatchRaw("m-3clk", match);
        ctx.SetupUserResolution("wu", "alice");
        ctx.SetupUserResolution("bu", "bob");
        ctx.SetupConvertSequenceToSan(InitialFen, ["e2e4", "e7e5", "g1f3"], ["e4", "e5", "Nf3"]);

        AnalysisGame game = await ctx.Service.ImportFromMatchAsync("m-3clk", "wu", CancellationToken.None);

        Assert.Contains(
            "1. e4 {[%clk 0:04:59]} e5 {[%clk 0:04:58]} 2. Nf3 {[%clk 0:04:57]}",
            game.Pgn,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ImportFromMatch_NoClockHistory_OmitsClkComments()
    {
        Struct match = new();
        match.Fields["status"] = Value.ForString("white_won");
        match.Fields["white_user_id"] = Value.ForString("wu");
        match.Fields["moves"] = Value.ForList([Value.ForString("e2e4")]);
        match.Fields["fen_history"] = Value.ForList([Value.ForString(InitialFen), Value.ForString("fen1")]);
        SetupMatchRaw("m-noclk", match);
        ctx.SetupUserResolution("wu", "alice");
        ctx.SetupConvertSequenceToSan(InitialFen, ["e2e4"], ["e4"]);

        AnalysisGame game = await ctx.Service.ImportFromMatchAsync("m-noclk", "wu", CancellationToken.None);

        Assert.Empty(game.ClockHistory);
        Assert.DoesNotContain("%clk", game.Pgn, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ImportFromMatch_ClockHistoryWrongKind_TreatedAsNoData()
    {
        Struct match = new();
        match.Fields["status"] = Value.ForString("white_won");
        match.Fields["white_user_id"] = Value.ForString("wu");
        match.Fields["clock_history"] = Value.ForString("not-a-list");
        SetupMatchRaw("m-badclk", match);
        ctx.SetupUserResolution("wu", "alice");

        AnalysisGame game = await ctx.Service.ImportFromMatchAsync("m-badclk", "wu", CancellationToken.None);

        Assert.Empty(game.ClockHistory);
    }

    [Fact]
    public async Task ImportFromMatch_ClockHistoryEntries_FilterNonStructsAndDefaultMissingFields()
    {
        Struct match = new();
        match.Fields["status"] = Value.ForString("white_won");
        match.Fields["white_user_id"] = Value.ForString("wu");
        match.Fields["clock_history"] = Value.ForList(
            [Clock(100, 200), MissingWhite(50), MissingBlack(60), Value.ForString("garbage")]);
        SetupMatchRaw("m-mixedclk", match);
        ctx.SetupUserResolution("wu", "alice");

        AnalysisGame game = await ctx.Service.ImportFromMatchAsync("m-mixedclk", "wu", CancellationToken.None);

        Assert.Equal(
            new[] { new ClockSnapshot(100, 200), new ClockSnapshot(0, 50), new ClockSnapshot(60, 0) },
            game.ClockHistory);
    }

    // ── ImportFromPgn / ImportFromFen ────────────────────────────────────────

    [Fact]
    public async Task ImportFromPgn_WithClkComments_PopulatesClockHistory()
    {
        const string afterE4 = "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1";
        const string afterE5 = "rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq e6 0 2";
        ctx.SetupValidateMoveSan(InitialFen, "e4", "e2e4", afterE4);
        ctx.SetupValidateMoveSan(afterE4, "e5", "e7e5", afterE5);

        const string pgn = "[White \"A\"]\n[Black \"B\"]\n\n1. e4 {[%clk 0:03:00]} e5 {[%clk 0:02:58]} *";

        AnalysisGame game = await ctx.Service.ImportFromPgnAsync(pgn, "user-1", CancellationToken.None);

        Assert.Equal(
            new[] { new ClockSnapshot(180_000, 0), new ClockSnapshot(180_000, 178_000) },
            game.ClockHistory);
    }

    [Fact]
    public async Task ImportFromPgn_NoClkComments_EmptyClockHistory()
    {
        const string afterE4 = "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1";
        ctx.SetupValidateMoveSan(InitialFen, "e4", "e2e4", afterE4);

        AnalysisGame game = await ctx.Service.ImportFromPgnAsync(
            "[White \"A\"]\n\n1. e4 *", "user-1", CancellationToken.None);

        Assert.Empty(game.ClockHistory);
    }

    [Fact]
    public async Task ImportFromFen_HasNoClockHistory()
    {
        AnalysisGame game = await ctx.Service.ImportFromFenAsync(InitialFen, "user-1", CancellationToken.None);

        Assert.Empty(game.ClockHistory);
    }

    private static Value Clock(long white, long black)
    {
        Struct s = new();
        s.Fields["white_time_ms"] = Value.ForNumber(white);
        s.Fields["black_time_ms"] = Value.ForNumber(black);
        return Value.ForStruct(s);
    }

    private static Value MissingWhite(long black)
    {
        Struct s = new();
        s.Fields["black_time_ms"] = Value.ForNumber(black);
        return Value.ForStruct(s);
    }

    private static Value MissingBlack(long white)
    {
        Struct s = new();
        s.Fields["white_time_ms"] = Value.ForNumber(white);
        return Value.ForStruct(s);
    }

    private void SetupMatchRaw(string matchId, Struct record) =>
        ctx.DbClient
            .GetAsync(
                Arg.Is<GetRequest>(r => r.Collection == "matches" && r.Id == matchId),
                Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(GrpcHelper.GrpcCall(new GetResponse { Record = record }));
}
