namespace MaichessAnalysisService.Domain;

// Which subset of a user's matches GET /matches returns. Defaults to Finished
// (the long-standing behaviour); Ongoing/All surface in-progress games so a
// player can open one for review without waiting for it to end.
internal enum UserMatchStatusFilter
{
    Finished,
    Ongoing,
    All,
}
