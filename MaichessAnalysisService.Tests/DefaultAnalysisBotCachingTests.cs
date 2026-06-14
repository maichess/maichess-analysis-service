using MaichessAnalysisService.Data;
using MaichessAnalysisService.Domain;
using MaichessAnalysisService.Services;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace MaichessAnalysisService.Tests;

// Task 26: the tier-5 knowledge ("classical") bot is the default analysis engine.
// These tests pin that the configured default is what drives L1 caching, and that
// bot_id participates in the cache key — so analysis cached under a previous default
// (e.g. "blitz") is never served for the new default. The cache key includes bot_id
// at both layers (Mongo filter {fen, bot_id} and Redis analysis:{botId}:{fen}), so a
// default change cannot surface stale lines from the old bot; the startup bot-change
// scrape additionally purges the previous default's entries.
public sealed class DefaultAnalysisBotCachingTests
{
    private const string KnowledgeClassical = "knowledge_classical";
    private const string PreviousDefault = "blitz";
    private const string Fen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    private readonly IAnalysisResultRepository inner = Substitute.For<IAnalysisResultRepository>();
    private readonly IAnalysisResultCache l1 = Substitute.For<IAnalysisResultCache>();
    private readonly CachingAnalysisResultRepository repo;

    public DefaultAnalysisBotCachingTests()
    {
        IOptions<AnalysisConfig> config =
            Options.Create(new AnalysisConfig { DefaultBotId = KnowledgeClassical });
        repo = new CachingAnalysisResultRepository(inner, l1, config);
    }

    private static AnalysisResultRecord Record(string botId) =>
        new(
            Id: $"r-{botId}",
            Fen: Fen,
            BotId: botId,
            LineCount: 3,
            Depth: 12,
            Lines: [new AnalysisLine(1, 30, ["e2e4"])],
            CreatedAt: DateTimeOffset.UnixEpoch);

    [Fact]
    public async Task DefaultBot_IsCachedInL1_KeyedByBotId()
    {
        l1.GetAsync(KnowledgeClassical, Fen, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<AnalysisResultRecord>?>(null));
        inner.GetCachedDepthsAsync(Fen, KnowledgeClassical, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<AnalysisResultRecord>>([Record(KnowledgeClassical)]));

        await repo.GetCachedDepthsAsync(Fen, KnowledgeClassical, CancellationToken.None);

        // L1 is consulted and promoted under the knowledge_classical key, never the old default.
        await l1.Received(1).GetAsync(KnowledgeClassical, Fen, Arg.Any<CancellationToken>());
        await l1.Received(1).SetAsync(
            KnowledgeClassical, Fen, Arg.Any<IReadOnlyList<AnalysisResultRecord>>(), Arg.Any<CancellationToken>());
        await l1.DidNotReceive().GetAsync(PreviousDefault, Fen, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PreviousDefaultBot_BypassesL1_AfterDefaultFlip()
    {
        inner.GetCachedDepthsAsync(Fen, PreviousDefault, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<AnalysisResultRecord>>([Record(PreviousDefault)]));

        await repo.GetCachedDepthsAsync(Fen, PreviousDefault, CancellationToken.None);

        // Once knowledge_classical is the default, the old default is a non-default bot:
        // it goes straight to Mongo and never touches the L1.
        await l1.DidNotReceive().GetAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await l1.DidNotReceive().SetAsync(
            Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<IReadOnlyList<AnalysisResultRecord>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InsertDepth_OnlyAppendsL1_ForKnowledgeClassicalDefault()
    {
        await repo.InsertDepthAsync(Record(KnowledgeClassical), CancellationToken.None);
        await repo.InsertDepthAsync(Record(PreviousDefault), CancellationToken.None);

        await l1.Received(1).AppendAsync(
            KnowledgeClassical, Fen, Arg.Any<AnalysisResultRecord>(), Arg.Any<CancellationToken>());
        await l1.DidNotReceive().AppendAsync(
            PreviousDefault, Fen, Arg.Any<AnalysisResultRecord>(), Arg.Any<CancellationToken>());
    }
}
