using MaichessAnalysisService.Data;
using MaichessAnalysisService.Domain;
using MaichessAnalysisService.Services;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace MaichessAnalysisService.Tests;

public sealed class CachingAnalysisResultRepositoryTests
{
    private const string DefaultBot = "stockfish-3";
    private const string Fen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    private readonly IAnalysisResultRepository inner = Substitute.For<IAnalysisResultRepository>();
    private readonly IAnalysisResultCache l1 = Substitute.For<IAnalysisResultCache>();
    private readonly CachingAnalysisResultRepository repo;

    public CachingAnalysisResultRepositoryTests()
    {
        IOptions<AnalysisConfig> config = Options.Create(new AnalysisConfig { DefaultBotId = DefaultBot });
        repo = new CachingAnalysisResultRepository(inner, l1, config);
    }

    private static AnalysisResultRecord Record(int depth, string botId = DefaultBot, int lineCount = 3) =>
        new(
            Id: $"r-{depth}",
            Fen: Fen,
            BotId: botId,
            LineCount: lineCount,
            Depth: depth,
            Lines: [new AnalysisLine(1, 12, ["e2e4"])],
            CreatedAt: DateTimeOffset.UnixEpoch);

    [Fact]
    public async Task GetCachedDepths_DefaultBot_L1Hit_ReturnsL1_WithoutTouchingL2()
    {
        IReadOnlyList<AnalysisResultRecord> hit = [Record(10), Record(12)];
        l1.GetAsync(DefaultBot, Fen, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<AnalysisResultRecord>?>(hit));

        IReadOnlyList<AnalysisResultRecord> result =
            await repo.GetCachedDepthsAsync(Fen, DefaultBot, CancellationToken.None);

        Assert.Same(hit, result);
        await inner.DidNotReceive().GetCachedDepthsAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await l1.DidNotReceive().SetAsync(
            Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<IReadOnlyList<AnalysisResultRecord>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetCachedDepths_DefaultBot_L1Miss_PromotesL2Hit()
    {
        IReadOnlyList<AnalysisResultRecord> fromL2 = [Record(10), Record(12)];
        l1.GetAsync(DefaultBot, Fen, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<AnalysisResultRecord>?>(null));
        inner.GetCachedDepthsAsync(Fen, DefaultBot, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(fromL2));

        IReadOnlyList<AnalysisResultRecord> result =
            await repo.GetCachedDepthsAsync(Fen, DefaultBot, CancellationToken.None);

        Assert.Same(fromL2, result);
        await l1.Received(1).SetAsync(DefaultBot, Fen, fromL2, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetCachedDepths_DefaultBot_L1Miss_L2Empty_DoesNotPromote()
    {
        l1.GetAsync(DefaultBot, Fen, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<AnalysisResultRecord>?>(null));
        inner.GetCachedDepthsAsync(Fen, DefaultBot, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<AnalysisResultRecord>>([]));

        IReadOnlyList<AnalysisResultRecord> result =
            await repo.GetCachedDepthsAsync(Fen, DefaultBot, CancellationToken.None);

        Assert.Empty(result);
        await l1.DidNotReceive().SetAsync(
            Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<IReadOnlyList<AnalysisResultRecord>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetCachedDepths_NonDefaultBot_BypassesL1()
    {
        const string otherBot = "stockfish-9";
        IReadOnlyList<AnalysisResultRecord> fromL2 = [Record(8, otherBot)];
        inner.GetCachedDepthsAsync(Fen, otherBot, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(fromL2));

        IReadOnlyList<AnalysisResultRecord> result =
            await repo.GetCachedDepthsAsync(Fen, otherBot, CancellationToken.None);

        Assert.Same(fromL2, result);
        await l1.DidNotReceive().GetAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await l1.DidNotReceive().SetAsync(
            Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<IReadOnlyList<AnalysisResultRecord>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InsertDepth_DefaultBot_WritesL2_AndAppendsL1()
    {
        AnalysisResultRecord record = Record(14);

        await repo.InsertDepthAsync(record, CancellationToken.None);

        await inner.Received(1).InsertDepthAsync(record, Arg.Any<CancellationToken>());
        await l1.Received(1).AppendAsync(DefaultBot, Fen, record, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InsertDepth_NonDefaultBot_WritesL2_Only()
    {
        AnalysisResultRecord record = Record(14, "stockfish-9");

        await repo.InsertDepthAsync(record, CancellationToken.None);

        await inner.Received(1).InsertDepthAsync(record, Arg.Any<CancellationToken>());
        await l1.DidNotReceive().AppendAsync(
            Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<AnalysisResultRecord>(), Arg.Any<CancellationToken>());
    }
}
