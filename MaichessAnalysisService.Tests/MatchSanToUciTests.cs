using MaichessAnalysisService.Services;
using Xunit;

namespace MaichessAnalysisService.Tests;

public sealed class MatchSanToUciTests
{
    private const string InitialFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
    private const string BlackToMoveFen = "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1";

    // ── Pawn moves ───────────────────────────────────────────────────────────

    [Fact]
    public void PawnPush_e4_ResolvesCorrectly()
    {
        string[] legal = ["e2e3", "e2e4", "d2d3", "d2d4"];
        string? result = AnalysisGameService.MatchSanToUci("e4", legal, InitialFen);
        Assert.Equal("e2e4", result);
    }

    [Fact]
    public void PawnPush_d3_ResolvesCorrectly()
    {
        string[] legal = ["d2d3", "d2d4", "e2e3", "e2e4"];
        string? result = AnalysisGameService.MatchSanToUci("d3", legal, InitialFen);
        Assert.Equal("d2d3", result);
    }

    [Fact]
    public void PawnCapture_exd5_ResolvesCorrectly()
    {
        // e4 pawn captures on d5
        string fen = "rnbqkbnr/ppp1pppp/8/3p4/4P3/8/PPPP1PPP/RNBQKBNR w KQkq d6 0 2";
        string[] legal = ["e4d5", "e4e5", "d2d3"];
        string? result = AnalysisGameService.MatchSanToUci("exd5", legal, fen);
        Assert.Equal("e4d5", result);
    }

    // ── Castling ─────────────────────────────────────────────────────────────

    [Fact]
    public void CastlingKingside_OO_ResolvesCorrectly()
    {
        string[] legal = ["e1g1", "e1f1", "d1d2"];
        string? result = AnalysisGameService.MatchSanToUci("O-O", legal, InitialFen);
        Assert.Equal("e1g1", result);
    }

    [Fact]
    public void CastlingQueenside_OOO_ResolvesCorrectly()
    {
        string[] legal = ["e1c1", "e1d1", "d1d2"];
        string? result = AnalysisGameService.MatchSanToUci("O-O-O", legal, InitialFen);
        Assert.Equal("e1c1", result);
    }

    [Fact]
    public void CastlingKingside_Black_ResolvesCorrectly()
    {
        string[] legal = ["e8g8", "e8f8"];
        string? result = AnalysisGameService.MatchSanToUci("O-O", legal, BlackToMoveFen);
        Assert.Equal("e8g8", result);
    }

    // ── Piece moves ──────────────────────────────────────────────────────────

    [Fact]
    public void KnightMove_Nf3_ResolvesCorrectly()
    {
        string[] legal = ["g1f3", "g1h3", "b1c3", "e2e4"];
        string? result = AnalysisGameService.MatchSanToUci("Nf3", legal, InitialFen);
        Assert.Equal("g1f3", result);
    }

    [Fact]
    public void BishopMove_Bc4_ResolvesCorrectly()
    {
        string fen = "rnbqkbnr/pppp1ppp/8/4p3/2B1P3/8/PPPP1PPP/RNBQK1NR w KQkq - 0 3";
        string[] legal = ["c4b3", "c4d3", "c4e2", "c4f1", "c4b5", "c4a6", "c4d5", "c4e6", "c4f7"];
        string? result = AnalysisGameService.MatchSanToUci("Bd5", legal, fen);
        Assert.Equal("c4d5", result);
    }

    [Fact]
    public void CheckSuffix_Stripped_BeforeMatching()
    {
        string[] legal = ["g1f3", "g1h3"];
        string? result = AnalysisGameService.MatchSanToUci("Nf3+", legal, InitialFen);
        Assert.Equal("g1f3", result);
    }

    [Fact]
    public void CheckmateSuffix_Stripped_BeforeMatching()
    {
        string[] legal = ["g1f3"];
        string? result = AnalysisGameService.MatchSanToUci("Nf3#", legal, InitialFen);
        Assert.Equal("g1f3", result);
    }

    // ── Promotions ───────────────────────────────────────────────────────────

    [Fact]
    public void Promotion_e8Q_ResolvesCorrectly()
    {
        string fen = "4k3/4P3/8/8/8/8/8/4K3 w - - 0 1";
        string[] legal = ["e7e8q", "e7e8r", "e7e8b", "e7e8n"];
        string? result = AnalysisGameService.MatchSanToUci("e8=Q", legal, fen);
        Assert.Equal("e7e8q", result);
    }

    [Fact]
    public void Promotion_e8N_ResolvesCorrectly()
    {
        string fen = "4k3/4P3/8/8/8/8/8/4K3 w - - 0 1";
        string[] legal = ["e7e8q", "e7e8r", "e7e8b", "e7e8n"];
        string? result = AnalysisGameService.MatchSanToUci("e8=N", legal, fen);
        Assert.Equal("e7e8n", result);
    }

    // ── Disambiguation ───────────────────────────────────────────────────────

    [Fact]
    public void DisambiguationByFile_Rad1_ResolvesCorrectly()
    {
        // Two rooks: a1 and f1, both can go to d1
        string fen = "4k3/8/8/8/8/8/8/R3KR2 w - - 0 1";
        string[] legal = ["a1d1", "f1d1", "a1b1", "f1e1"];
        string? result = AnalysisGameService.MatchSanToUci("Rad1", legal, fen);
        Assert.Equal("a1d1", result);
    }

    [Fact]
    public void DisambiguationByRank_R1d3_ResolvesCorrectly()
    {
        // Two white rooks: d1 and d6, both can reach d3 — rank digit disambiguates
        string fen = "4k3/8/3R4/8/8/8/8/3RK3 w - - 0 1";
        string[] legal = ["d1d3", "d6d3", "d1d2", "d6d7"];
        string? result = AnalysisGameService.MatchSanToUci("R1d3", legal, fen);
        Assert.Equal("d1d3", result);
    }

    [Fact]
    public void DisambiguationByFullSquare_Qd1e2_ResolvesCorrectly()
    {
        // Full square disambiguation: Qd1e2
        string fen = "4k3/8/8/8/8/8/8/3QKQ2 w - - 0 1";
        string[] legal = ["d1e2", "f1e2"];
        string? result = AnalysisGameService.MatchSanToUci("Qd1e2", legal, fen);
        Assert.Equal("d1e2", result);
    }

    // ── Black moves ──────────────────────────────────────────────────────────

    [Fact]
    public void BlackPawnPush_e5_ResolvesCorrectly()
    {
        string[] legal = ["e7e6", "e7e5", "d7d6", "d7d5"];
        string? result = AnalysisGameService.MatchSanToUci("e5", legal, BlackToMoveFen);
        Assert.Equal("e7e5", result);
    }

    [Fact]
    public void BlackKnightMove_Nf6_ResolvesCorrectly()
    {
        string[] legal = ["g8f6", "g8h6", "b8c6"];
        string? result = AnalysisGameService.MatchSanToUci("Nf6", legal, BlackToMoveFen);
        Assert.Equal("g8f6", result);
    }

    // ── FEN edge cases ───────────────────────────────────────────────────────

    [Fact]
    public void FenWithNoActiveColorPart_DefaultsToWhite()
    {
        string fenPositionOnly = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR";
        string[] legal = ["e2e4"];
        string? result = AnalysisGameService.MatchSanToUci("e4", legal, fenPositionOnly);
        Assert.Equal("e2e4", result);
    }

    // ── GetPieceAt edge cases ─────────────────────────────────────────────────

    [Fact]
    public void GetPieceAt_ShortSquare_ReturnsNull()
    {
        Assert.Null(AnalysisGameService.GetPieceAt(InitialFen, "a"));
    }

    [Fact]
    public void GetPieceAt_OutOfBoundsRank_ReturnsNull()
    {
        Assert.Null(AnalysisGameService.GetPieceAt(InitialFen, "a9"));
    }

    [Fact]
    public void GetPieceAt_EmptySquare_ReturnsNull()
    {
        Assert.Null(AnalysisGameService.GetPieceAt(InitialFen, "e4"));
    }

    // ── No match ─────────────────────────────────────────────────────────────

    [Fact]
    public void NoMatch_ReturnsNull()
    {
        string[] legal = ["e2e4", "d2d4"];
        string? result = AnalysisGameService.MatchSanToUci("Nf6", legal, InitialFen);
        Assert.Null(result);
    }
}
