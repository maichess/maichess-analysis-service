namespace MaichessAnalysisService.Domain;

internal sealed class InvalidPgnException(string reason)
    : Exception(reason)
{
    internal string Reason { get; } = reason;
}
