using System.Globalization;
using MaichessAnalysisService.Domain;

namespace MaichessAnalysisService.Rest;

internal static class AnalysisGameMapper
{
    internal static GameSummaryResponse ToSummary(AnalysisGame game) =>
        new(
            game.Id,
            game.Source,
            game.MatchId,
            game.White,
            game.Black,
            game.Result,
            game.Moves.Count,
            game.CreatedAt.ToString("O", CultureInfo.InvariantCulture),
            game.Tags);

    internal static GameDetailResponse ToDetail(AnalysisGame game) =>
        new(
            game.Id,
            game.Source,
            game.StartingFen,
            game.MatchId,
            game.White,
            game.Black,
            game.Result,
            game.Moves.Count,
            game.CreatedAt.ToString("O", CultureInfo.InvariantCulture),
            game.Tags,
            game.Moves,
            game.Fens,
            game.Pgn,
            [.. game.ClockHistory.Select(c => new ClockSnapshotResponse(c.WhiteTimeMs, c.BlackTimeMs))]);

    internal static UserMatchSummaryResponse ToUserMatchSummary(UserMatchSummary match) =>
        new(
            match.MatchId,
            match.White,
            match.Black,
            match.Status,
            new UserMatchTimeFormatResponse(
                match.TimeFormat.Id,
                match.TimeFormat.BaseMs,
                match.TimeFormat.IncrementMs,
                match.TimeFormat.Category),
            match.MoveCount,
            match.FinishedAtMs);
}
