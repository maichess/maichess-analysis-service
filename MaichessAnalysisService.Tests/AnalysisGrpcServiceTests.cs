using Grpc.Core;
using Maichess.Analysis.V1;
using Maichess.Engine.V1;
using MaichessAnalysisService.Grpc;
using MaichessAnalysisService.Tests.Support;
using NSubstitute;
using Xunit;

namespace MaichessAnalysisService.Tests;

public sealed class AnalysisGrpcServiceTests
{
    [Fact]
    public async Task StreamPositionAnalysis_RelaysUpdatesCorrectly()
    {
        Bots.BotsClient botsClient = Substitute.For<Bots.BotsClient>();
        AnalysisGrpcService service = new(botsClient);

        AnalysisUpdate update = new() { Depth = 10 };
        PrincipalVariation pv = new() { Rank = 1, EvaluationCp = 50 };
        pv.Moves.AddRange(["e2e4", "e7e5"]);
        update.Lines.Add(pv);

        TestAsyncStreamReader<AnalysisUpdate> engineStream = new([update]);
        AsyncServerStreamingCall<AnalysisUpdate> engineCall = new(
            engineStream,
            Task.FromResult(Metadata.Empty),
            () => Status.DefaultSuccess,
            () => Metadata.Empty,
            () => { });

        botsClient
            .AnalyzePosition(
                Arg.Any<AnalyzePositionRequest>(),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns(engineCall);

        TestServerStreamWriter<PositionAnalysisUpdate> writer = new();
        TestServerCallContext ctx = TestServerCallContext.Create();

        await service.StreamPositionAnalysis(
            new StreamPositionAnalysisRequest { Fen = "startpos", BotId = "bot-1", LineCount = 1 },
            writer,
            ctx);

        Assert.Single(writer.Written);
        PositionAnalysisUpdate relayed = writer.Written[0];
        Assert.Equal(10u, relayed.Depth);
        Assert.Single(relayed.Lines);
        Assert.Equal(1u, relayed.Lines[0].Rank);
        Assert.Equal(50, relayed.Lines[0].EvaluationCp);
        Assert.Equal(["e2e4", "e7e5"], relayed.Lines[0].Moves);
    }

    [Fact]
    public async Task StreamPositionAnalysis_MultipleUpdates_AllRelayed()
    {
        Bots.BotsClient botsClient = Substitute.For<Bots.BotsClient>();
        AnalysisGrpcService service = new(botsClient);

        AnalysisUpdate update1 = new() { Depth = 5 };
        AnalysisUpdate update2 = new() { Depth = 10 };

        TestAsyncStreamReader<AnalysisUpdate> engineStream = new([update1, update2]);
        AsyncServerStreamingCall<AnalysisUpdate> engineCall = new(
            engineStream,
            Task.FromResult(Metadata.Empty),
            () => Status.DefaultSuccess,
            () => Metadata.Empty,
            () => { });

        botsClient
            .AnalyzePosition(
                Arg.Any<AnalyzePositionRequest>(),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns(engineCall);

        TestServerStreamWriter<PositionAnalysisUpdate> writer = new();
        TestServerCallContext ctx = TestServerCallContext.Create();

        await service.StreamPositionAnalysis(
            new StreamPositionAnalysisRequest { Fen = "startpos", BotId = "bot-1", LineCount = 1 },
            writer,
            ctx);

        Assert.Equal(2, writer.Written.Count);
        Assert.Equal(5u, writer.Written[0].Depth);
        Assert.Equal(10u, writer.Written[1].Depth);
    }

    [Fact]
    public async Task StreamPositionAnalysis_Cancellation_StopsStream()
    {
        Bots.BotsClient botsClient = Substitute.For<Bots.BotsClient>();
        AnalysisGrpcService service = new(botsClient);

        using CancellationTokenSource cts = new();
        TestServerCallContext ctx = TestServerCallContext.Create(cts.Token);

        AnalysisUpdate update = new() { Depth = 1 };
        CancellingStreamReader<AnalysisUpdate> engineStream = new([update], cts);
        AsyncServerStreamingCall<AnalysisUpdate> engineCall = new(
            engineStream,
            Task.FromResult(Metadata.Empty),
            () => Status.DefaultSuccess,
            () => Metadata.Empty,
            () => { });

        botsClient
            .AnalyzePosition(
                Arg.Any<AnalyzePositionRequest>(),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns(engineCall);

        TestServerStreamWriter<PositionAnalysisUpdate> writer = new();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            service.StreamPositionAnalysis(
                new StreamPositionAnalysisRequest { Fen = "startpos", BotId = "bot-1", LineCount = 1 },
                writer,
                ctx));
    }

    private sealed class CancellingStreamReader<T>(IEnumerable<T> items, CancellationTokenSource cts)
        : IAsyncStreamReader<T>
    {
        private readonly Queue<T> queue = new(items);
        private bool cancelledAfterFirst;

        public T Current { get; private set; } = default!;

        public Task<bool> MoveNext(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (queue.Count == 0)
            {
                return Task.FromResult(false);
            }

            Current = queue.Dequeue();
            if (!cancelledAfterFirst)
            {
                cancelledAfterFirst = true;
                cts.Cancel();
            }

            return Task.FromResult(true);
        }
    }
}
