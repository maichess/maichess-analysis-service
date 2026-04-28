using Grpc.Core;
using Maichess.MatchManager.V1;
using Maichess.MoveValidator.V1;
using MaichessAnalysisService.Domain;
using MaichessAnalysisService.Services;
using NSubstitute;

namespace MaichessAnalysisService.Tests.Support;

internal sealed class AnalysisServiceContext
{
    internal IAnalysisGameRepository Repository { get; } = Substitute.For<IAnalysisGameRepository>();

    internal Matches.MatchesClient MatchesClient { get; } = Substitute.For<Matches.MatchesClient>();

    internal Moves.MovesClient MovesClient { get; } = Substitute.For<Moves.MovesClient>();

    internal AnalysisGameService Service { get; }

    internal AnalysisGame? LastGameResult { get; set; }

    internal (IReadOnlyList<AnalysisGame> Games, int Total, int Page, int PageSize)? LastListResult { get; set; }

    internal Exception? LastException { get; set; }

    internal AnalysisServiceContext()
    {
        Service = new AnalysisGameService(Repository, MatchesClient, MovesClient);

        Repository.InsertAsync(Arg.Any<AnalysisGame>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult(ci.Arg<AnalysisGame>() with { Id = "game-1" }));
    }

    internal void SetupGame(AnalysisGame game)
    {
        Repository.GetByIdAsync(game.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<AnalysisGame?>(game));
    }

    internal void SetupGameNotFound(string id)
    {
        Repository.GetByIdAsync(id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<AnalysisGame?>(null));
    }

    internal void SetupList(string userId, IReadOnlyList<AnalysisGame> games, int total)
    {
        Repository.CountByUserIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(total));
        Repository.ListByUserIdAsync(userId, Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                int limit = ci.ArgAt<int>(1);
                int offset = ci.ArgAt<int>(2);
                IReadOnlyList<AnalysisGame> slice = games.Skip(offset).Take(limit).ToList();
                return Task.FromResult(slice);
            });
    }

    internal void SetupLegalMoves(string fen, IEnumerable<string> moves)
    {
        GetLegalMovesResponse response = new();
        response.Moves.AddRange(moves);

        MovesClient
            .GetLegalMovesAsync(
                Arg.Is<GetLegalMovesRequest>(r => r.Fen == fen),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns(GrpcHelper.GrpcCall(response));
    }

    internal void SetupValidateMove(string fen, string move, string resultingFen)
    {
        ValidateMoveResponse response = new()
        {
            ResultingFen = resultingFen,
        };
        response.PositionHistory.Add(resultingFen);

        MovesClient
            .ValidateMoveAsync(
                Arg.Is<ValidateMoveRequest>(r => r.Fen == fen && r.Move == move),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns(GrpcHelper.GrpcCall(response));
    }

    internal void SetupMatchNotFound(string matchId)
    {
        MatchesClient
            .GetMatchAsync(
                Arg.Is<GetMatchRequest>(r => r.MatchId == matchId),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns<AsyncUnaryCall<GetMatchResponse>>(_ =>
                throw new RpcException(new Status(StatusCode.NotFound, "not found")));
    }

    internal void SetupMatch(Maichess.MatchManager.V1.Match match)
    {
        GetMatchResponse response = new() { Match = match };

        MatchesClient
            .GetMatchAsync(
                Arg.Is<GetMatchRequest>(r => r.MatchId == match.Id),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns(GrpcHelper.GrpcCall(response));
    }

    internal void SetupMatchPosition(string matchId, int index, string fen)
    {
        GetMatchPositionResponse response = new() { Fen = fen };

        MatchesClient
            .GetMatchPositionAsync(
                Arg.Is<GetMatchPositionRequest>(r => r.MatchId == matchId && r.Index == index),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns(GrpcHelper.GrpcCall(response));
    }

    internal static AnalysisGame BuildGame(
        string id = "game-1",
        string userId = "user-1",
        string source = "pgn") =>
        new(
            Id: id,
            UserId: userId,
            Source: source,
            MatchId: null,
            Moves: ["e2e4", "e7e5"],
            Fens: [
                "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1",
                "rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq e6 0 2",
            ],
            Pgn: "[Event \"Test\"]\n\n1. e4 e5",
            Result: "*",
            White: new Dictionary<string, string> { ["name"] = "White" },
            Black: new Dictionary<string, string> { ["name"] = "Black" },
            Tags: new Dictionary<string, string> { ["Event"] = "Test" },
            CreatedAt: DateTimeOffset.UtcNow);
}
