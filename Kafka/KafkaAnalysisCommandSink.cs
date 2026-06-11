using System.Diagnostics.CodeAnalysis;
using Confluent.Kafka;
using Maichess.Events.V1;
using MaichessAnalysisService.Services;

namespace MaichessAnalysisService.Kafka;

// Publishes StartAnalysis / StopAnalysis to analysis.commands.v1 (keyed by
// sessionId), replacing the synchronous Engine.AnalyzePosition gRPC stream. The
// engine consumes these, runs the iterative-deepening search, and streams depth
// updates back over analysis.events.v1 (handled by AnalysisEventConsumer).
// Fire-and-forget over the broker: the caller has already updated session state
// and emitted any cached depths, so it does not block on the publish.
[ExcludeFromCodeCoverage]
internal sealed class KafkaAnalysisCommandSink : IAnalysisCommandSink, IDisposable
{
    private const string Topic = "analysis.commands.v1";
    private const string ProducerName = "analysis-service";

    private readonly IProducer<string, AnalysisCommand> producer;
    private readonly ILogger<KafkaAnalysisCommandSink> logger;

    public KafkaAnalysisCommandSink(ILogger<KafkaAnalysisCommandSink> logger)
    {
        this.logger = logger;

        string bootstrap = Environment.GetEnvironmentVariable("KAFKA_BOOTSTRAP") ?? "kafka:9092";

        producer = new ProducerBuilder<string, AnalysisCommand>(
                new ProducerConfig { BootstrapServers = bootstrap })
            .SetValueSerializer(ProtobufEventSerdes.Serializer<AnalysisCommand>())
            .Build();
    }

    public Task StartAsync(string sessionId, string fen, string botId, int lineCount)
    {
        AnalysisCommand command = Envelope(sessionId, "analysis.StartAnalysis");
        command.StartAnalysis = new StartAnalysisCommand
        {
            SessionId = sessionId,
            Fen = fen,
            BotId = botId,
            LineCount = lineCount,
        };
        return ProduceAsync(sessionId, command, "StartAnalysis");
    }

    public Task StopAsync(string sessionId)
    {
        AnalysisCommand command = Envelope(sessionId, "analysis.StopAnalysis");
        command.StopAnalysis = new StopAnalysisCommand { SessionId = sessionId };
        return ProduceAsync(sessionId, command, "StopAnalysis");
    }

    public void Dispose()
    {
        producer.Flush(TimeSpan.FromSeconds(5));
        producer.Dispose();
    }

    private static AnalysisCommand Envelope(string sessionId, string eventType) => new()
    {
        EventId = Guid.NewGuid().ToString(),
        EventType = eventType,
        AggregateId = sessionId,
        Sequence = 0L,
        OccurredAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        CorrelationId = string.Empty,
        CausationId = string.Empty,
        Producer = ProducerName,
    };

#pragma warning disable CA1031 // Fire-and-forget background publish: log and swallow all failures.
    private async Task ProduceAsync(string sessionId, AnalysisCommand command, string kind)
    {
        try
        {
            await producer.ProduceAsync(
                Topic, new Message<string, AnalysisCommand> { Key = sessionId, Value = command });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to publish {Kind} to {Topic}", kind, Topic);
        }
    }
#pragma warning restore CA1031
}
