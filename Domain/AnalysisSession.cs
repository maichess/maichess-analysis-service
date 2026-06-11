namespace MaichessAnalysisService.Domain;

internal sealed class AnalysisSession
{
    internal AnalysisSession(
        string id,
        string userId,
        string gameId,
        string botId,
        int lineCount,
        AnalysisGame game)
    {
        Id = id;
        UserId = userId;
        GameId = gameId;
        BotId = botId;
        LineCount = lineCount;
        Game = game;
    }

    internal string Id { get; }

    internal string UserId { get; }

    internal string GameId { get; }

    internal string BotId { get; set; }

    internal int LineCount { get; set; }

    internal int CurrentIndex { get; set; }

    internal List<string> WhatifMoves { get; } = [];

    internal List<string> WhatifFens { get; } = [];

    internal CancellationTokenSource? ActiveCts { get; set; }

    // Kafka transport only: the position the in-flight analysis was started for
    // (null when no analysis is running). AnalysisEventConsumer drops events whose
    // fen no longer matches, so a depth update from a superseded run (navigate /
    // whatif) is ignored. MaxCachedDepth is the deepest cached depth already
    // emitted at start time, so live depths at or below it are not re-sent.
    internal string? AnalyzedFen { get; set; }

    internal int MaxCachedDepth { get; set; }

    internal AnalysisGame Game { get; }

    internal string GetCurrentFen() =>
        WhatifFens.Count > 0 ? WhatifFens[^1] : GetBaseFen();

    internal string GetBaseFen() =>
        CurrentIndex == 0 ? Game.StartingFen : Game.Fens[CurrentIndex - 1];
}
