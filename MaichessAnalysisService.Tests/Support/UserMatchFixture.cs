namespace MaichessAnalysisService.Tests.Support;

internal sealed record UserMatchFixture(
    string Id,
    string Status,
    string? WhiteUserId,
    string? BlackUserId,
    string? WhiteBotId,
    string? BlackBotId,
    string TimeFormatId,
    long BaseMs,
    long IncrementMs,
    string Category,
    DateTimeOffset LastMoveAt,
    IReadOnlyList<string> Moves);
