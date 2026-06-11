namespace MaichessAnalysisService.Services;

// Transport seam for analysis session control. The gRPC path streams from the
// engine directly inside AnalysisSessionService; when a sink is registered
// (KAFKA_ENABLED), control is published to analysis.commands.v1 instead and the
// depth updates arrive back over analysis.events.v1. Keeping this a seam lets the
// session service stay transport-agnostic and the Kafka glue stay excluded.
internal interface IAnalysisCommandSink
{
    // Begin analysis of `fen` for `sessionId` (keyed by sessionId for ordering).
    Task StartAsync(string sessionId, string fen, string botId, int lineCount);

    // Cancel the running analysis for `sessionId`.
    Task StopAsync(string sessionId);
}
