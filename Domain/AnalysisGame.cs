namespace MaichessAnalysisService.Domain;

internal sealed record AnalysisGame(
    string Id,
    string UserId,
    string Source,
    string? MatchId,
    string StartingFen,
    IReadOnlyList<string> Moves,
    IReadOnlyList<string> Fens,
    string Pgn,
    string Result,
    IReadOnlyDictionary<string, string> White,
    IReadOnlyDictionary<string, string> Black,
    IReadOnlyDictionary<string, string> Tags,
    DateTimeOffset CreatedAt,

    // Per-move remaining-clock snapshots parallel to Moves; empty when the source had
    // no clock data. Carried from a match document's clock_history on match import, or
    // parsed from {[%clk ...]}/{[%emt ...]} comments on PGN import.
    IReadOnlyList<ClockSnapshot> ClockHistory);
