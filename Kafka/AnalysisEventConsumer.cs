using System.Diagnostics.CodeAnalysis;
using Confluent.Kafka;
using Maichess.Events.V1;
using MaichessAnalysisService.Domain;
using MaichessAnalysisService.Services;

namespace MaichessAnalysisService.Kafka;

// Consumes analysis.events.v1 and forwards engine depth updates to the client via
// the shared socket connection. The engine produces these in response to the
// StartAnalysis commands published by KafkaAnalysisCommandSink; this closes the
// loop that the synchronous Engine.AnalyzePosition gRPC stream used to. It runs in
// the same process as the producer, so it resolves the live in-memory session by
// id (AnalysisSessionService) to recover the user and stale-position filter.
// Latest offset reset: depth updates are ephemeral live state, not a log to replay.
[ExcludeFromCodeCoverage]
internal sealed class AnalysisEventConsumer : BackgroundService
{
    private const string Topic = "analysis.events.v1";

    // Unique group per process: analysis sessions live in-memory on the replica
    // that started them, so every replica must see every event and deliver only the
    // ones whose session it holds (FindById). A shared group would shard partitions
    // across replicas and strand updates on the wrong node. Latest offset reset —
    // these are ephemeral live updates, not a log to replay on restart.
    private static readonly string GroupId = $"analysis-service-{Guid.NewGuid():N}";

    private readonly AnalysisSessionService sessionService;
    private readonly ILogger<AnalysisEventConsumer> logger;
    private readonly IConsumer<string, AnalysisEvent> consumer;

    public AnalysisEventConsumer(
        AnalysisSessionService sessionService,
        ILogger<AnalysisEventConsumer> logger)
    {
        this.sessionService = sessionService;
        this.logger = logger;

        string bootstrap = Environment.GetEnvironmentVariable("KAFKA_BOOTSTRAP") ?? "kafka:9092";

        consumer = new ConsumerBuilder<string, AnalysisEvent>(new ConsumerConfig
        {
            BootstrapServers = bootstrap,
            GroupId = GroupId,
            AutoOffsetReset = AutoOffsetReset.Latest,
        })
            .SetValueDeserializer(ProtobufEventSerdes.Deserializer<AnalysisEvent>())
            .Build();
    }

    public override void Dispose()
    {
        consumer.Dispose();
        base.Dispose();
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.Run(() => ConsumeLoop(stoppingToken), stoppingToken);

    private static IReadOnlyList<AnalysisLine> ToLines(AnalysisDepthCompleted depth) =>
        [.. depth.Lines.Select(pv => new AnalysisLine(pv.Rank, pv.EvaluationCp, [.. pv.Moves]))];

#pragma warning disable CA1031 // Resilient consumer loop: log and continue on per-message failures.
    private void ConsumeLoop(CancellationToken ct)
    {
        consumer.Subscribe(Topic);
        try
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    ConsumeResult<string, AnalysisEvent> result = consumer.Consume(ct);
                    if (result?.Message?.Value is { } evt)
                    {
                        Dispatch(evt, ct).GetAwaiter().GetResult();
                    }
                }
                catch (ConsumeException ex)
                {
                    logger.LogWarning(ex, "Error consuming {Topic}", Topic);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Error handling analysis event");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // graceful shutdown
        }
        finally
        {
            consumer.Close();
        }
    }
#pragma warning restore CA1031

    private Task Dispatch(AnalysisEvent evt, CancellationToken ct) => evt.PayloadCase switch
    {
        AnalysisEvent.PayloadOneofCase.AnalysisDepthCompleted => sessionService.OnDepthAsync(
            evt.AnalysisDepthCompleted.SessionId,
            evt.AnalysisDepthCompleted.Fen,
            evt.AnalysisDepthCompleted.BotId,
            evt.AnalysisDepthCompleted.Depth,
            ToLines(evt.AnalysisDepthCompleted),
            ct),
        AnalysisEvent.PayloadOneofCase.AnalysisCompleted => sessionService.OnCompleteAsync(
            evt.AnalysisCompleted.SessionId, evt.AnalysisCompleted.FinalDepth, ct),
        AnalysisEvent.PayloadOneofCase.AnalysisFailed => sessionService.OnFailedAsync(
            evt.AnalysisFailed.SessionId, evt.AnalysisFailed.Message, ct),
        _ => Task.CompletedTask,
    };
}
