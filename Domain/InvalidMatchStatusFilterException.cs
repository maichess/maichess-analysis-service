namespace MaichessAnalysisService.Domain;

internal sealed class InvalidMatchStatusFilterException(string value)
    : Exception($"invalid status filter: {value}")
{
    internal string Value { get; } = value;
}
