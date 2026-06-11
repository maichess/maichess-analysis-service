using MaichessAnalysisService.Domain;

namespace MaichessAnalysisService.Services;

// Transport seam for pushing analysis results to a single client. Replaces the
// direct Socket.EmitEvent gRPC call (removed in Kafka task 09): the sole
// implementation (KafkaSocketPushSink) produces an OutboundEvent to
// socket.outbound.v1, which the socket service fans out to the target user.
// Keeping this a seam lets AnalysisSessionService stay transport-agnostic and the
// Kafka glue stay excluded from coverage.
internal interface ISocketPushSink
{
    // Deliver an analysis_update (a depth's principal variations) to userId.
    Task PushAnalysisUpdateAsync(
        string userId, string sessionId, int depth, IReadOnlyList<AnalysisLine> lines, CancellationToken ct);

    // Deliver an analysis_complete (search finished at finalDepth) to userId.
    Task PushAnalysisCompleteAsync(string userId, string sessionId, int finalDepth, CancellationToken ct);

    // Deliver an analysis_error (search failed) to userId.
    Task PushAnalysisErrorAsync(string userId, string sessionId, string message, CancellationToken ct);
}
