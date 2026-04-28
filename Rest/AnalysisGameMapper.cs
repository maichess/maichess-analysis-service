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
            game.MatchId,
            game.White,
            game.Black,
            game.Result,
            game.Moves.Count,
            game.CreatedAt.ToString("O", CultureInfo.InvariantCulture),
            game.Tags,
            game.Moves,
            game.Fens,
            game.Pgn);
}
