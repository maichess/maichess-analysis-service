using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace MaichessAnalysisService.Rest;

[ExcludeFromCodeCoverage]
internal sealed record WhatifPgnResponse(
    [property: JsonPropertyName("pgn")] string Pgn);
