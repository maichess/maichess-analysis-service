using Maichess.Engine.V1;
using Maichess.MoveValidator.V1;
using MaichessAnalysisService.Domain;
using MaichessAnalysisService.Services;
using MaichessAnalysisService.Tests.Support;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace MaichessAnalysisService.Tests;

// Session lifecycle, whatif branches, the synchronous gRPC analysis stream and the
// Kafka command path. The fire-and-forget dispatch (Task.Run) is driven through the
// public entry points and synchronised on the mocked downstream calls so the
// background work is observed deterministically.
public sealed class AnalysisSessionServiceTests
{
    private const string DefaultBot = "default-bot";
    private const int DefaultLineCount = 3;
    private const string UserId = "user-1";
    private const string GameId = "game-1";
    private const string StartFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
    private const string Fen1 = "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1";
    private const string Fen2 = "rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq e6 0 2";

    private readonly IAnalysisGameRepository gameRepo = Substitute.For<IAnalysisGameRepository>();
    private readonly IAnalysisResultRepository resultRepo = Substitute.For<IAnalysisResultRepository>();
    private readonly Bots.BotsClient bots = Substitute.For<Bots.BotsClient>();
    private readonly Moves.MovesClient moves = Substitute.For<Moves.MovesClient>();
    private readonly ISocketPushSink push = Substitute.For<ISocketPushSink>();
    private readonly IAnalysisCommandSink sink = Substitute.For<IAnalysisCommandSink>();

    public AnalysisSessionServiceTests() =>
        resultRepo.GetCachedDepthsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<AnalysisResultRecord>>([]));

    private AnalysisSessionService BuildService(bool withSink = false) =>
        new(
            gameRepo,
            resultRepo,
            bots,
            moves,
            push,
            Options.Create(new AnalysisConfig { DefaultBotId = DefaultBot, DefaultLineCount = DefaultLineCount }),
            withSink ? [sink] : []);

    private static AnalysisGame Game(
        string userId = UserId,
        string? startFen = null) =>
        new(
            Id: GameId,
            UserId: userId,
            Source: "pgn",
            MatchId: null,
            StartingFen: startFen ?? StartFen,
            Moves: ["e2e4", "e7e5"],
            Fens: [Fen1, Fen2],
            Pgn: "[Event \"T\"]",
            Result: "*",
            White: new Dictionary<string, string>(),
            Black: new Dictionary<string, string>(),
            Tags: new Dictionary<string, string>(),
            CreatedAt: DateTimeOffset.UnixEpoch,
            ClockHistory: []);

    private async Task<(AnalysisSessionService Svc, AnalysisSession Session)> CreateAsync(
        bool withSink = false, AnalysisGame? game = null, int lineCount = DefaultLineCount)
    {
        game ??= Game();
        gameRepo.GetByIdAsync(GameId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<AnalysisGame?>(game));
        AnalysisSessionService svc = BuildService(withSink);
        AnalysisSession session =
            await svc.CreateSessionAsync(UserId, GameId, DefaultBot, lineCount, CancellationToken.None);
        return (svc, session);
    }

    // Lifecycle methods (navigate/whatif/start) kick a fire-and-forget analysis run;
    // give the engine an empty stream so that background work completes cleanly.
    private void SetupEmptyEngineStream() =>
        bots.AnalyzePosition(
                Arg.Any<AnalyzePositionRequest>(), Arg.Any<Grpc.Core.Metadata>(),
                Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(_ => GrpcHelper.ServerStream(Array.Empty<AnalysisUpdate>()));

    private void SetupValidMove(string resultingFen) =>
        moves.ValidateMoveAsync(
                Arg.Any<ValidateMoveRequest>(), Arg.Any<Grpc.Core.Metadata>(),
                Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(GrpcHelper.GrpcCall(new ValidateMoveResponse { Valid = true, ResultingFen = resultingFen }));

    private static AnalysisUpdate Update(int depth, params (int Rank, int Eval, string Move)[] lines)
    {
        AnalysisUpdate u = new() { Depth = (uint)depth };
        foreach ((int rank, int eval, string move) in lines)
        {
            PrincipalVariation pv = new() { Rank = (uint)rank, EvaluationCp = eval };
            pv.Moves.Add(move);
            u.Lines.Add(pv);
        }

        return u;
    }

    private static AnalysisResultRecord Record(int depth, int lineCount = DefaultLineCount, string botId = DefaultBot) =>
        new(
            Id: $"r-{depth}",
            Fen: StartFen,
            BotId: botId,
            LineCount: lineCount,
            Depth: depth,
            Lines: [new AnalysisLine(1, 10, ["e2e4"]), new AnalysisLine(2, 5, ["d2d4"])],
            CreatedAt: DateTimeOffset.UnixEpoch);

    // ── CreateSession ────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateSession_GameNotFound_Throws()
    {
        gameRepo.GetByIdAsync(GameId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<AnalysisGame?>(null));
        AnalysisSessionService svc = BuildService();

        await Assert.ThrowsAsync<AnalysisGameNotFoundException>(() =>
            svc.CreateSessionAsync(UserId, GameId, DefaultBot, 3, CancellationToken.None));
    }

    [Fact]
    public async Task CreateSession_WrongUser_ThrowsAccessDenied()
    {
        gameRepo.GetByIdAsync(GameId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<AnalysisGame?>(Game(userId: "someone-else")));
        AnalysisSessionService svc = BuildService();

        await Assert.ThrowsAsync<AccessDeniedException>(() =>
            svc.CreateSessionAsync(UserId, GameId, DefaultBot, 3, CancellationToken.None));
    }

    [Fact]
    public async Task CreateSession_Succeeds_PopulatesSession()
    {
        (_, AnalysisSession session) = await CreateAsync();

        Assert.StartsWith("s-", session.Id, StringComparison.Ordinal);
        Assert.Equal(UserId, session.UserId);
        Assert.Equal(GameId, session.GameId);
        Assert.Equal(DefaultBot, session.BotId);
        Assert.Equal(DefaultLineCount, session.LineCount);
        Assert.Equal(0, session.CurrentIndex);
    }

    [Fact]
    public async Task CreateSession_ReplacesExistingSessionForUser_AndCancelsItsRun()
    {
        (AnalysisSessionService svc, AnalysisSession first) = await CreateAsync();
        SetupEmptyEngineStream();
        await svc.StartAnalysisAsync(first.Id, UserId, null, null, CancellationToken.None);
        CancellationTokenSource firstRun = first.ActiveCts!;

        AnalysisSession second =
            await svc.CreateSessionAsync(UserId, GameId, DefaultBot, 3, CancellationToken.None);

        Assert.NotEqual(first.Id, second.Id);
        // Replacing the session cancels the previous analysis run.
        Assert.True(firstRun.IsCancellationRequested);
        // The replaced session is no longer addressable.
        await Assert.ThrowsAsync<SessionNotFoundException>(() =>
            svc.NavigateAsync(first.Id, UserId, 0, CancellationToken.None));
    }

    // ── DestroySession / GetSession ──────────────────────────────────────────

    [Fact]
    public async Task DestroySession_RemovesSession_AndCancelsItsRun()
    {
        (AnalysisSessionService svc, AnalysisSession session) = await CreateAsync();
        SetupEmptyEngineStream();
        await svc.StartAnalysisAsync(session.Id, UserId, null, null, CancellationToken.None);
        CancellationTokenSource run = session.ActiveCts!;

        await svc.DestroySessionAsync(session.Id, UserId, CancellationToken.None);

        Assert.True(run.IsCancellationRequested);
        await Assert.ThrowsAsync<SessionNotFoundException>(() =>
            svc.NavigateAsync(session.Id, UserId, 0, CancellationToken.None));
    }

    [Fact]
    public async Task DestroySession_UnknownSession_Throws()
    {
        (AnalysisSessionService svc, _) = await CreateAsync();

        await Assert.ThrowsAsync<SessionNotFoundException>(() =>
            svc.DestroySessionAsync("nope", UserId, CancellationToken.None));
    }

    [Fact]
    public async Task GetSession_WrongUser_Throws()
    {
        (AnalysisSessionService svc, AnalysisSession session) = await CreateAsync();

        await Assert.ThrowsAsync<SessionNotFoundException>(() =>
            svc.NavigateAsync(session.Id, "other-user", 0, CancellationToken.None));
    }

    // ── Navigate ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public async Task Navigate_OutOfRange_Throws(int index)
    {
        (AnalysisSessionService svc, AnalysisSession session) = await CreateAsync();

        await Assert.ThrowsAsync<NavigationOutOfRangeException>(() =>
            svc.NavigateAsync(session.Id, UserId, index, CancellationToken.None));
    }

    [Fact]
    public async Task Navigate_ToStart_ReturnsStartingFen_AndRestartsAnalysis()
    {
        (AnalysisSessionService svc, AnalysisSession session) = await CreateAsync();
        SetupEmptyEngineStream();

        (int Index, string Fen) result = await svc.NavigateAsync(session.Id, UserId, 0, CancellationToken.None);

        Assert.Equal(0, result.Index);
        Assert.Equal(StartFen, result.Fen);
        // Navigation restarts analysis at the new position.
        Assert.NotNull(session.ActiveCts);
    }

    [Fact]
    public async Task Navigate_Again_CancelsThePreviousRun()
    {
        (AnalysisSessionService svc, AnalysisSession session) = await CreateAsync();
        SetupEmptyEngineStream();

        await svc.NavigateAsync(session.Id, UserId, 0, CancellationToken.None);
        CancellationTokenSource first = session.ActiveCts!;
        await svc.NavigateAsync(session.Id, UserId, 1, CancellationToken.None);

        Assert.True(first.IsCancellationRequested);
        Assert.NotSame(first, session.ActiveCts);
    }

    [Fact]
    public async Task Navigate_ToLastMove_ReturnsFenAndClearsWhatif()
    {
        (AnalysisSessionService svc, AnalysisSession session) = await CreateAsync();
        session.WhatifMoves.Add("x");
        session.WhatifFens.Add("y");

        (int Index, string Fen) result = await svc.NavigateAsync(session.Id, UserId, 2, CancellationToken.None);

        Assert.Equal(2, result.Index);
        Assert.Equal(Fen2, result.Fen);
        Assert.Empty(session.WhatifMoves);
        Assert.Empty(session.WhatifFens);
    }

    // ── PlayWhatif / Reset / Undo ────────────────────────────────────────────

    [Fact]
    public async Task PlayWhatif_InvalidMove_Throws()
    {
        (AnalysisSessionService svc, AnalysisSession session) = await CreateAsync();
        moves.ValidateMoveAsync(
                Arg.Any<ValidateMoveRequest>(), Arg.Any<Grpc.Core.Metadata>(),
                Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(GrpcHelper.GrpcCall(new ValidateMoveResponse { Valid = false, Reason = "illegal" }));

        InvalidWhatifMoveException ex = await Assert.ThrowsAsync<InvalidWhatifMoveException>(() =>
            svc.PlayWhatifAsync(session.Id, UserId, "e2e5", CancellationToken.None));
        Assert.Equal("illegal", ex.Reason);
    }

    [Fact]
    public async Task PlayWhatif_Valid_AppendsMoveAndFen()
    {
        (AnalysisSessionService svc, AnalysisSession session) = await CreateAsync();
        SetupEmptyEngineStream();
        SetupValidMove("after-fen");

        (int WhatifIndex, string Fen) result =
            await svc.PlayWhatifAsync(session.Id, UserId, "e2e4", CancellationToken.None);

        Assert.Equal(1, result.WhatifIndex);
        Assert.Equal("after-fen", result.Fen);
        Assert.Equal(["e2e4"], session.WhatifMoves);
        // Validation runs against the current position and analysis restarts.
        _ = moves.Received(1).ValidateMoveAsync(
            Arg.Is<ValidateMoveRequest>(r => r.Fen == StartFen && r.Move == "e2e4"),
            Arg.Any<Grpc.Core.Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>());
        Assert.NotNull(session.ActiveCts);
    }

    [Fact]
    public async Task ResetWhatif_ClearsBranch()
    {
        (AnalysisSessionService svc, AnalysisSession session) = await CreateAsync();
        SetupEmptyEngineStream();
        session.WhatifMoves.Add("e2e4");
        session.WhatifFens.Add("after-fen");

        (int Index, string Fen) result = await svc.ResetWhatifAsync(session.Id, UserId, CancellationToken.None);

        Assert.Equal(0, result.Index);
        Assert.Equal(StartFen, result.Fen);
        Assert.Empty(session.WhatifMoves);
        Assert.NotNull(session.ActiveCts);
    }

    [Fact]
    public async Task UndoLastWhatif_Empty_Throws()
    {
        (AnalysisSessionService svc, AnalysisSession session) = await CreateAsync();

        await Assert.ThrowsAsync<WhatifEmptyException>(() =>
            svc.UndoLastWhatifAsync(session.Id, UserId, CancellationToken.None));
    }

    [Fact]
    public async Task UndoLastWhatif_RemovesLast()
    {
        (AnalysisSessionService svc, AnalysisSession session) = await CreateAsync();
        session.WhatifMoves.Add("e2e4");
        session.WhatifMoves.Add("e7e5");
        session.WhatifFens.Add("f1");
        session.WhatifFens.Add("f2");

        (int WhatifIndex, string Fen) result =
            await svc.UndoLastWhatifAsync(session.Id, UserId, CancellationToken.None);

        Assert.Equal(1, result.WhatifIndex);
        Assert.Equal("f1", result.Fen);
        Assert.Equal(["e2e4"], session.WhatifMoves);
    }

    // ── GetWhatifPgn / BuildWhatifPgn ────────────────────────────────────────

    [Fact]
    public async Task GetWhatifPgn_Empty_Throws()
    {
        (AnalysisSessionService svc, AnalysisSession session) = await CreateAsync();

        await Assert.ThrowsAsync<WhatifEmptyException>(() =>
            svc.GetWhatifPgnAsync(session.Id, UserId, CancellationToken.None));
    }

    [Fact]
    public async Task GetWhatifPgn_WhiteToMove_BuildsNumberedPgn()
    {
        const string baseFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 5";
        (AnalysisSessionService svc, AnalysisSession session) =
            await CreateAsync(game: Game(startFen: baseFen));
        session.WhatifMoves.Add("e2e4");
        moves.ConvertSequenceToSanAsync(
                Arg.Any<ConvertSequenceToSanRequest>(), Arg.Any<Grpc.Core.Metadata>(),
                Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(GrpcHelper.GrpcCall(SanResponse("e4", "e5")));

        string pgn = await svc.GetWhatifPgnAsync(session.Id, UserId, CancellationToken.None);

        Assert.Contains($"[FEN \"{baseFen}\"]", pgn, StringComparison.Ordinal);
        Assert.Contains("[SetUp \"1\"]", pgn, StringComparison.Ordinal);
        Assert.Contains("5. e4 e5", pgn, StringComparison.Ordinal);
        Assert.EndsWith("*", pgn, StringComparison.Ordinal);
        // A blank line separates the headers from the move text.
        Assert.Contains("\n\n", pgn.Replace("\r\n", "\n", StringComparison.Ordinal), StringComparison.Ordinal);
        // The conversion is requested from the whatif base position for the whatif moves.
        _ = moves.Received(1).ConvertSequenceToSanAsync(
            Arg.Is<ConvertSequenceToSanRequest>(r => r.StartingFen == baseFen && r.UciMoves.Contains("e2e4")),
            Arg.Any<Grpc.Core.Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetWhatifPgn_TwoFieldFen_ReadsSideToMove()
    {
        // Exactly two FEN fields: the side-to-move field is still honoured (boundary
        // for the "< 2 fields" malformed guard).
        (AnalysisSessionService svc, AnalysisSession session) =
            await CreateAsync(game: Game(startFen: "8/8/8/8/8/8/8/8 b"));
        session.WhatifMoves.Add("e7e5");
        moves.ConvertSequenceToSanAsync(
                Arg.Any<ConvertSequenceToSanRequest>(), Arg.Any<Grpc.Core.Metadata>(),
                Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(GrpcHelper.GrpcCall(SanResponse("e5")));

        string pgn = await svc.GetWhatifPgnAsync(session.Id, UserId, CancellationToken.None);

        Assert.Contains("1... e5", pgn, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetWhatifPgn_BlackToMove_OnlyFirstMoveGetsEllipsis()
    {
        (AnalysisSessionService svc, AnalysisSession session) =
            await CreateAsync(game: Game(startFen: "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR b KQkq - 0 5"));
        session.WhatifMoves.Add("e7e5");
        moves.ConvertSequenceToSanAsync(
                Arg.Any<ConvertSequenceToSanRequest>(), Arg.Any<Grpc.Core.Metadata>(),
                Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(GrpcHelper.GrpcCall(SanResponse("e5", "Nf3", "Nc6")));

        string pgn = await svc.GetWhatifPgnAsync(session.Id, UserId, CancellationToken.None);

        // Only the leading black move is prefixed with the ellipsis; the later black
        // move is bare.
        Assert.Contains("5... e5 6. Nf3 Nc6", pgn, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetWhatifPgn_BlackToMove_StartsWithEllipsis()
    {
        (AnalysisSessionService svc, AnalysisSession session) =
            await CreateAsync(game: Game(startFen: "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR b KQkq - 0 5"));
        session.WhatifMoves.Add("e7e5");
        moves.ConvertSequenceToSanAsync(
                Arg.Any<ConvertSequenceToSanRequest>(), Arg.Any<Grpc.Core.Metadata>(),
                Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(GrpcHelper.GrpcCall(SanResponse("e5", "Nf3")));

        string pgn = await svc.GetWhatifPgnAsync(session.Id, UserId, CancellationToken.None);

        Assert.Contains("5... e5", pgn, StringComparison.Ordinal);
        Assert.Contains("6. Nf3", pgn, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetWhatifPgn_MalformedBaseFen_DefaultsToWhiteMoveOne()
    {
        (AnalysisSessionService svc, AnalysisSession session) =
            await CreateAsync(game: Game(startFen: "onlyboard"));
        session.WhatifMoves.Add("e2e4");
        moves.ConvertSequenceToSanAsync(
                Arg.Any<ConvertSequenceToSanRequest>(), Arg.Any<Grpc.Core.Metadata>(),
                Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(GrpcHelper.GrpcCall(SanResponse("e4")));

        string pgn = await svc.GetWhatifPgnAsync(session.Id, UserId, CancellationToken.None);

        Assert.Contains("1. e4", pgn, StringComparison.Ordinal);
    }

    private static ConvertSequenceToSanResponse SanResponse(params string[] sans)
    {
        ConvertSequenceToSanResponse resp = new();
        resp.SanMoves.AddRange(sans);
        return resp;
    }

    // ── Start / Stop overrides ───────────────────────────────────────────────

    [Fact]
    public async Task StartAnalysis_AppliesBotAndLineCountOverrides()
    {
        (AnalysisSessionService svc, AnalysisSession session) = await CreateAsync();
        bots.AnalyzePosition(
                Arg.Any<AnalyzePositionRequest>(), Arg.Any<Grpc.Core.Metadata>(),
                Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(GrpcHelper.ServerStream(Array.Empty<AnalysisUpdate>()));

        await svc.StartAnalysisAsync(session.Id, UserId, "other-bot", 5, CancellationToken.None);

        Assert.Equal("other-bot", session.BotId);
        Assert.Equal(5, session.LineCount);
        Assert.NotNull(session.ActiveCts);
    }

    [Fact]
    public async Task StartAnalysis_NoOverrides_KeepsValues()
    {
        (AnalysisSessionService svc, AnalysisSession session) = await CreateAsync();
        bots.AnalyzePosition(
                Arg.Any<AnalyzePositionRequest>(), Arg.Any<Grpc.Core.Metadata>(),
                Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(GrpcHelper.ServerStream(Array.Empty<AnalysisUpdate>()));

        await svc.StartAnalysisAsync(session.Id, UserId, null, null, CancellationToken.None);

        Assert.Equal(DefaultBot, session.BotId);
        Assert.Equal(DefaultLineCount, session.LineCount);
    }

    [Fact]
    public async Task StartAnalysis_RestartCancelsThePreviousRun()
    {
        (AnalysisSessionService svc, AnalysisSession session) = await CreateAsync();
        SetupEmptyEngineStream();

        await svc.StartAnalysisAsync(session.Id, UserId, null, null, CancellationToken.None);
        CancellationTokenSource first = session.ActiveCts!;
        await svc.StartAnalysisAsync(session.Id, UserId, null, null, CancellationToken.None);

        Assert.True(first.IsCancellationRequested);
    }

    [Fact]
    public async Task StopAnalysis_GrpcPath_CancelsActiveRun()
    {
        (AnalysisSessionService svc, AnalysisSession session) = await CreateAsync();
        SetupEmptyEngineStream();
        await svc.StartAnalysisAsync(session.Id, UserId, null, null, CancellationToken.None);
        CancellationTokenSource active = session.ActiveCts!;

        await svc.StopAnalysisAsync(session.Id, UserId, CancellationToken.None);

        Assert.True(active.IsCancellationRequested);
        Assert.Null(session.ActiveCts);
    }

    // ── RunAnalysisStreamAsync (gRPC path, exercised directly) ───────────────

    [Fact]
    public async Task RunAnalysisStream_EmitsCached_StreamsDeeperDepths_Completes()
    {
        (AnalysisSessionService svc, AnalysisSession session) = await CreateAsync();
        // Two cached depths: the deepest (8) is the cache ceiling.
        resultRepo.GetCachedDepthsAsync(StartFen, DefaultBot, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<AnalysisResultRecord>>([Record(8), Record(5)]));
        bots.AnalyzePosition(
                Arg.Any<AnalyzePositionRequest>(), Arg.Any<Grpc.Core.Metadata>(),
                Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(GrpcHelper.ServerStream([Update(8, (1, 12, "e2e4")), Update(9, (1, 20, "d2d4"))]));

        await svc.RunAnalysisStreamAsync(session, CancellationToken.None);

        // The engine is queried for exactly this position / bot / line count.
        _ = bots.Received(1).AnalyzePosition(
            Arg.Is<AnalyzePositionRequest>(r =>
                r.Fen == StartFen && r.BotId == DefaultBot && r.LineCount == (uint)DefaultLineCount),
            Arg.Any<Grpc.Core.Metadata>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>());
        // Cached depths are emitted ascending (5 before 8) and trimmed to the line count.
        Received.InOrder(() =>
        {
            push.PushAnalysisUpdateAsync(
                UserId, session.Id, 5, Arg.Any<IReadOnlyList<AnalysisLine>>(), Arg.Any<CancellationToken>());
            push.PushAnalysisUpdateAsync(
                UserId, session.Id, 8, Arg.Any<IReadOnlyList<AnalysisLine>>(), Arg.Any<CancellationToken>());
        });
        await push.Received(1).PushAnalysisUpdateAsync(
            UserId, session.Id, 5, Arg.Is<IReadOnlyList<AnalysisLine>>(l => l.Count == 2), Arg.Any<CancellationToken>());
        // The live depth 8 is at the cache ceiling, so it is dropped (emitted once, from cache).
        await push.Received(1).PushAnalysisUpdateAsync(
            UserId, session.Id, 8, Arg.Any<IReadOnlyList<AnalysisLine>>(), Arg.Any<CancellationToken>());
        // Only the deeper live depth 9 is delivered and completes the search.
        await push.Received(1).PushAnalysisUpdateAsync(
            UserId, session.Id, 9, Arg.Any<IReadOnlyList<AnalysisLine>>(), Arg.Any<CancellationToken>());
        await push.Received(1).PushAnalysisCompleteAsync(UserId, session.Id, 9, Arg.Any<CancellationToken>());
        // Default bot + default line count → the new depth is persisted with an empty (server-assigned) id.
        await resultRepo.Received(1).InsertDepthAsync(
            Arg.Is<AnalysisResultRecord>(r => r.Depth == 9 && r.Id == string.Empty), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAnalysisStream_NonDefaultBot_DoesNotPersist()
    {
        (AnalysisSessionService svc, AnalysisSession session) = await CreateAsync();
        session.BotId = "other-bot";
        bots.AnalyzePosition(
                Arg.Any<AnalyzePositionRequest>(), Arg.Any<Grpc.Core.Metadata>(),
                Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(GrpcHelper.ServerStream([Update(6, (1, 20, "d2d4"))]));

        await svc.RunAnalysisStreamAsync(session, CancellationToken.None);

        await push.Received(1).PushAnalysisUpdateAsync(
            UserId, session.Id, 6, Arg.Any<IReadOnlyList<AnalysisLine>>(), Arg.Any<CancellationToken>());
        await resultRepo.DidNotReceive().InsertDepthAsync(
            Arg.Any<AnalysisResultRecord>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAnalysisStream_Cancelled_SwallowsAndDoesNotError()
    {
        (AnalysisSessionService svc, AnalysisSession session) = await CreateAsync();
        bots.AnalyzePosition(
                Arg.Any<AnalyzePositionRequest>(), Arg.Any<Grpc.Core.Metadata>(),
                Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(GrpcHelper.ServerStream([Update(6, (1, 20, "d2d4"))]));
        using CancellationTokenSource cts = new();
        await cts.CancelAsync();

        await svc.RunAnalysisStreamAsync(session, cts.Token);

        await push.DidNotReceive().PushAnalysisErrorAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await push.DidNotReceive().PushAnalysisCompleteAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAnalysisStream_EngineThrows_EmitsError()
    {
        (AnalysisSessionService svc, AnalysisSession session) = await CreateAsync();
        resultRepo.GetCachedDepthsAsync(StartFen, DefaultBot, Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyList<AnalysisResultRecord>>>(_ => throw new InvalidOperationException("boom"));

        await svc.RunAnalysisStreamAsync(session, CancellationToken.None);

        await push.Received(1).PushAnalysisErrorAsync(
            UserId, session.Id, "boom", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAnalysisStream_FiltersCachedByLineCount()
    {
        (AnalysisSessionService svc, AnalysisSession session) = await CreateAsync(lineCount: 5);
        // A cached depth at a smaller line count must not count toward the cached max.
        resultRepo.GetCachedDepthsAsync(StartFen, DefaultBot, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<AnalysisResultRecord>>([Record(9, lineCount: 2)]));
        bots.AnalyzePosition(
                Arg.Any<AnalyzePositionRequest>(), Arg.Any<Grpc.Core.Metadata>(),
                Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Returns(GrpcHelper.ServerStream([Update(1, (1, 12, "e2e4"))]));

        await svc.RunAnalysisStreamAsync(session, CancellationToken.None);

        // Cached record (line count 2 < 5) is ignored, so the shallow live depth 1 is delivered.
        await push.DidNotReceive().PushAnalysisUpdateAsync(
            UserId, session.Id, 9, Arg.Any<IReadOnlyList<AnalysisLine>>(), Arg.Any<CancellationToken>());
        await push.Received(1).PushAnalysisUpdateAsync(
            UserId, session.Id, 1, Arg.Any<IReadOnlyList<AnalysisLine>>(), Arg.Any<CancellationToken>());
    }

    // ── Kafka-path callbacks (OnDepth / OnComplete / OnFailed) ───────────────

    [Fact]
    public async Task OnDepth_UnknownSession_Ignored()
    {
        (AnalysisSessionService svc, _) = await CreateAsync();

        await svc.OnDepthAsync("ghost", StartFen, DefaultBot, 7, [], CancellationToken.None);

        await push.DidNotReceive().PushAnalysisUpdateAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(),
            Arg.Any<IReadOnlyList<AnalysisLine>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnDepth_FenMismatch_Ignored()
    {
        (AnalysisSessionService svc, AnalysisSession session) = await CreateAsync();
        session.AnalyzedFen = "a-different-fen";

        await svc.OnDepthAsync(session.Id, StartFen, DefaultBot, 7, [], CancellationToken.None);

        await push.DidNotReceive().PushAnalysisUpdateAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(),
            Arg.Any<IReadOnlyList<AnalysisLine>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnDepth_AtOrBelowCachedDepth_Ignored()
    {
        (AnalysisSessionService svc, AnalysisSession session) = await CreateAsync();
        session.AnalyzedFen = StartFen;
        session.MaxCachedDepth = 10;

        await svc.OnDepthAsync(session.Id, StartFen, DefaultBot, 10, [], CancellationToken.None);

        await push.DidNotReceive().PushAnalysisUpdateAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(),
            Arg.Any<IReadOnlyList<AnalysisLine>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnDepth_DeeperThanCached_Delivers()
    {
        (AnalysisSessionService svc, AnalysisSession session) = await CreateAsync();
        session.AnalyzedFen = StartFen;
        session.MaxCachedDepth = 4;
        IReadOnlyList<AnalysisLine> lines = [new AnalysisLine(1, 15, ["e2e4"])];

        await svc.OnDepthAsync(session.Id, StartFen, DefaultBot, 7, lines, CancellationToken.None);

        await push.Received(1).PushAnalysisUpdateAsync(
            UserId, session.Id, 7, Arg.Any<IReadOnlyList<AnalysisLine>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnComplete_UnknownSession_Ignored()
    {
        (AnalysisSessionService svc, _) = await CreateAsync();

        await svc.OnCompleteAsync("ghost", 9, CancellationToken.None);

        await push.DidNotReceive().PushAnalysisCompleteAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnComplete_NoActiveAnalysis_Ignored()
    {
        (AnalysisSessionService svc, AnalysisSession session) = await CreateAsync();
        // AnalyzedFen stays null → there is no live run to complete.

        await svc.OnCompleteAsync(session.Id, 9, CancellationToken.None);

        await push.DidNotReceive().PushAnalysisCompleteAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnComplete_ActiveAnalysis_Emits()
    {
        (AnalysisSessionService svc, AnalysisSession session) = await CreateAsync();
        session.AnalyzedFen = StartFen;

        await svc.OnCompleteAsync(session.Id, 9, CancellationToken.None);

        await push.Received(1).PushAnalysisCompleteAsync(UserId, session.Id, 9, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnFailed_UnknownSession_Ignored()
    {
        (AnalysisSessionService svc, _) = await CreateAsync();

        await svc.OnFailedAsync("ghost", "bad", CancellationToken.None);

        await push.DidNotReceive().PushAnalysisErrorAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnFailed_KnownSession_Emits()
    {
        (AnalysisSessionService svc, AnalysisSession session) = await CreateAsync();

        await svc.OnFailedAsync(session.Id, "bad", CancellationToken.None);

        await push.Received(1).PushAnalysisErrorAsync(UserId, session.Id, "bad", Arg.Any<CancellationToken>());
    }

    // ── Kafka command path (StartViaKafka / CancelAnalysis via sink) ─────────

    [Fact]
    public async Task StartAnalysis_KafkaPath_EmitsCachedThenStartsSink()
    {
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        sink.StartAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>())
            .Returns(_ => { started.TrySetResult(); return Task.CompletedTask; });
        resultRepo.GetCachedDepthsAsync(StartFen, DefaultBot, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<AnalysisResultRecord>>([Record(5)]));
        (AnalysisSessionService svc, AnalysisSession session) = await CreateAsync(withSink: true);

        await svc.StartAnalysisAsync(session.Id, UserId, null, null, CancellationToken.None);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(StartFen, session.AnalyzedFen);
        Assert.Equal(5, session.MaxCachedDepth);
        await sink.Received(1).StartAsync(session.Id, StartFen, DefaultBot, DefaultLineCount);
        await push.Received(1).PushAnalysisUpdateAsync(
            UserId, session.Id, 5, Arg.Any<IReadOnlyList<AnalysisLine>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAnalysis_KafkaPath_OnError_Emits()
    {
        TaskCompletionSource errored = new(TaskCreationOptions.RunContinuationsAsynchronously);
        push.PushAnalysisErrorAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => { errored.TrySetResult(); return Task.CompletedTask; });
        resultRepo.GetCachedDepthsAsync(StartFen, DefaultBot, Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyList<AnalysisResultRecord>>>(_ => throw new InvalidOperationException("kaboom"));
        (AnalysisSessionService svc, AnalysisSession session) = await CreateAsync(withSink: true);

        await svc.StartAnalysisAsync(session.Id, UserId, null, null, CancellationToken.None);
        await errored.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await push.Received(1).PushAnalysisErrorAsync(UserId, session.Id, "kaboom", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StopAnalysis_KafkaPath_StopsSink()
    {
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
        sink.StartAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>())
            .Returns(_ => { started.TrySetResult(); return Task.CompletedTask; });
        sink.StopAsync(Arg.Any<string>())
            .Returns(_ => { stopped.TrySetResult(); return Task.CompletedTask; });
        (AnalysisSessionService svc, AnalysisSession session) = await CreateAsync(withSink: true);

        await svc.StartAnalysisAsync(session.Id, UserId, null, null, CancellationToken.None);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await svc.StopAnalysisAsync(session.Id, UserId, CancellationToken.None);
        await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Null(session.AnalyzedFen);
        await sink.Received(1).StopAsync(session.Id);
    }
}
