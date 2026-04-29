namespace MaichessAnalysisService.Domain;

internal sealed class SessionNotFoundException()
    : Exception("Session not found");
