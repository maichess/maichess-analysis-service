using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using MaichessAnalysisService.Domain;

namespace MaichessAnalysisService.Rest;

[ExcludeFromCodeCoverage]
internal sealed record SessionResponse(
    [property: JsonPropertyName("session_id")] string SessionId,
    [property: JsonPropertyName("game_id")] string GameId,
    [property: JsonPropertyName("current_index")] int CurrentIndex,
    [property: JsonPropertyName("current_fen")] string CurrentFen,
    [property: JsonPropertyName("whatif_moves")] IReadOnlyList<string> WhatifMoves,
    [property: JsonPropertyName("analysis_running")] bool AnalysisRunning)
{
    internal static SessionResponse FromSession(AnalysisSession session) =>
        new(
            session.Id,
            session.GameId,
            session.CurrentIndex,
            session.GetCurrentFen(),
            session.WhatifMoves,
            session.ActiveCts is not null);
}
