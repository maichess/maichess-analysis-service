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

    internal AnalysisGame Game { get; }

    internal string GetCurrentFen() =>
        WhatifFens.Count > 0 ? WhatifFens[^1] : GetBaseFen();

    internal string GetBaseFen() =>
        CurrentIndex == 0 ? Game.StartingFen : Game.Fens[CurrentIndex - 1];
}
