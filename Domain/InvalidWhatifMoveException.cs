namespace MaichessAnalysisService.Domain;

internal sealed class InvalidWhatifMoveException(string reason)
    : Exception(reason)
{
    internal string Reason { get; } = reason;
}
