namespace MaichessAnalysisService.Domain;

// A per-move remaining-clock snapshot, parallel to an AnalysisGame's Moves:
// ClockHistory[i] holds the clocks after Moves[i]. Empty when the source game
// carried no clock data (a FEN import, a PGN with no clock comments, or a match
// document that predates clock history).
internal sealed record ClockSnapshot(long WhiteTimeMs, long BlackTimeMs);
