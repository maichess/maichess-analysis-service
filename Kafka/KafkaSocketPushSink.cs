using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Confluent.Kafka;
using Maichess.Events.V1;
using MaichessAnalysisService.Domain;
using MaichessAnalysisService.Services;

namespace MaichessAnalysisService.Kafka;

// Pushes analysis results to a single client by producing an OutboundEvent to the
// socket.outbound.v1 Kafka topic (keyed/targeted by user_id). The socket service
// consumes the topic and fans out to that user, replacing the direct
// Socket.EmitEvent gRPC call (removed in Kafka task 09). Payloads are JSON-encoded
// in payload_json with the same field names the legacy gRPC Struct used, so the
// shape delivered to clients (session_id, depth, lines, …) is unchanged.
//
// Serialized as raw Protobuf bytes (Kafka task 09 removed the Schema Registry).
[ExcludeFromCodeCoverage]
internal sealed class KafkaSocketPushSink : ISocketPushSink, IDisposable
{
    private const string Topic = "socket.outbound.v1";
    private const string ProducerName = "analysis-service";

    private readonly IProducer<string, OutboundEvent> producer;
    private readonly ILogger<KafkaSocketPushSink> logger;

    public KafkaSocketPushSink(ILogger<KafkaSocketPushSink> logger)
    {
        this.logger = logger;

        string bootstrap = Environment.GetEnvironmentVariable("KAFKA_BOOTSTRAP") ?? "kafka:9092";

        producer = new ProducerBuilder<string, OutboundEvent>(
                new ProducerConfig { BootstrapServers = bootstrap })
            .SetValueSerializer(ProtobufEventSerdes.Serializer<OutboundEvent>())
            .Build();
    }

    public Task PushAnalysisUpdateAsync(
        string userId, string sessionId, int depth, IReadOnlyList<AnalysisLine> lines, CancellationToken ct)
    {
        Dictionary<string, object?> payload = new()
        {
            ["session_id"] = sessionId,
            ["depth"] = depth,
            ["lines"] = lines.Select(l => new Dictionary<string, object?>
            {
                ["rank"] = l.Rank,
                ["evaluation_cp"] = l.EvaluationCp,
                ["moves"] = l.Moves,
            }).ToList(),
        };
        return PushToUserAsync(userId, "analysis_update", payload, ct);
    }

    public Task PushAnalysisCompleteAsync(string userId, string sessionId, int finalDepth, CancellationToken ct)
    {
        Dictionary<string, object?> payload = new()
        {
            ["session_id"] = sessionId,
            ["final_depth"] = finalDepth,
        };
        return PushToUserAsync(userId, "analysis_complete", payload, ct);
    }

    public Task PushAnalysisErrorAsync(string userId, string sessionId, string message, CancellationToken ct)
    {
        Dictionary<string, object?> payload = new()
        {
            ["session_id"] = sessionId,
            ["message"] = message,
        };
        return PushToUserAsync(userId, "analysis_error", payload, ct);
    }

    public void Dispose()
    {
        producer.Flush(TimeSpan.FromSeconds(5));
        producer.Dispose();
    }

    private async Task PushToUserAsync(
        string userId, string eventName, Dictionary<string, object?> payload, CancellationToken ct)
    {
        OutboundEvent envelope = new()
        {
            EventId = Guid.NewGuid().ToString(),
            EventType = $"socket.{eventName}",
            AggregateId = userId,
            Sequence = 0L,
            OccurredAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Producer = ProducerName,
            Push = new SocketPush
            {
                TargetUserId = userId,
                EventName = eventName,
                PayloadJson = JsonSerializer.Serialize(payload),
            },
        };

        try
        {
            await producer.ProduceAsync(
                Topic, new Message<string, OutboundEvent> { Key = userId, Value = envelope }, ct);
        }
#pragma warning disable CA1031 // Best-effort client push: log and swallow all failures.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            logger.LogWarning(ex, "Failed to push {Event} for {User}", eventName, userId);
        }
    }
}
