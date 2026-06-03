using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Maichess.Database.V1;
using Maichess.Engine.V1;
using Maichess.MoveValidator.V1;
using Maichess.User.V1;
using MaichessAnalysisService.Domain;
using MaichessAnalysisService.Services;
using NSubstitute;

namespace MaichessAnalysisService.Tests.Support;

internal sealed class AnalysisServiceContext
{
    internal IAnalysisGameRepository Repository { get; } = Substitute.For<IAnalysisGameRepository>();

    internal Database.DatabaseClient DbClient { get; } = Substitute.For<Database.DatabaseClient>();

    internal Moves.MovesClient MovesClient { get; } = Substitute.For<Moves.MovesClient>();

    internal Users.UsersClient UsersClient { get; } = Substitute.For<Users.UsersClient>();

    internal Bots.BotsClient BotsClient { get; } = Substitute.For<Bots.BotsClient>();

    internal AnalysisGameService Service { get; }

    internal AnalysisGame? LastGameResult { get; set; }

    internal (IReadOnlyList<AnalysisGame> Games, long Total, int Page, int PageSize)? LastListResult { get; set; }

    internal (IReadOnlyList<UserMatchSummary> Matches, long Total, int Page, int PageSize)? LastUserMatchesResult { get; set; }

    internal Exception? LastException { get; set; }

    internal AnalysisServiceContext()
    {
        Service = new AnalysisGameService(Repository, DbClient, MovesClient, UsersClient, BotsClient);

        SetupDefaultUserResolution();
        SetupDefaultBotResolution();

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

    internal void SetupList(string userId, IReadOnlyList<AnalysisGame> games, long total)
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

    internal void SetupValidateMoveSan(string fen, string san, string uciMove, string resultingFen)
    {
        ValidateMoveSanResponse response = new()
        {
            Valid = true,
            UciMove = uciMove,
            ResultingFen = resultingFen,
        };
        response.PositionHistory.Add(resultingFen);

        MovesClient
            .ValidateMoveSanAsync(
                Arg.Is<ValidateMoveSanRequest>(r => r.Fen == fen && r.Move == san),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns(GrpcHelper.GrpcCall(response));
    }

    internal void SetupValidateMoveSanInvalid(string san, string reason)
    {
        ValidateMoveSanResponse response = new() { Valid = false, Reason = reason };

        MovesClient
            .ValidateMoveSanAsync(
                Arg.Is<ValidateMoveSanRequest>(r => r.Move == san),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns(GrpcHelper.GrpcCall(response));
    }

    internal void SetupConvertSequenceToSan(string startingFen, IReadOnlyList<string> uciMoves, IReadOnlyList<string> sanMoves)
    {
        ConvertSequenceToSanResponse response = new();
        response.SanMoves.AddRange(sanMoves);

        MovesClient
            .ConvertSequenceToSanAsync(
                Arg.Is<ConvertSequenceToSanRequest>(r =>
                    r.StartingFen == startingFen &&
                    r.UciMoves.SequenceEqual(uciMoves)),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns(GrpcHelper.GrpcCall(response));
    }

    internal void SetupMatch(
        string matchId,
        string status,
        string? whiteUserId,
        string? blackUserId,
        string? whiteBotId,
        string? blackBotId,
        IReadOnlyList<string> moves,
        IReadOnlyList<string> fenHistory,
        string? createdByUserId = null)
    {
        Struct matchStruct = new();
        matchStruct.Fields["status"] = Value.ForString(status);
        if (createdByUserId is not null)
        {
            matchStruct.Fields["created_by_user_id"] = Value.ForString(createdByUserId);
        }

        if (whiteUserId is not null)
        {
            matchStruct.Fields["white_user_id"] = Value.ForString(whiteUserId);
        }

        if (blackUserId is not null)
        {
            matchStruct.Fields["black_user_id"] = Value.ForString(blackUserId);
        }

        if (whiteBotId is not null)
        {
            matchStruct.Fields["white_bot_id"] = Value.ForString(whiteBotId);
        }

        if (blackBotId is not null)
        {
            matchStruct.Fields["black_bot_id"] = Value.ForString(blackBotId);
        }

        matchStruct.Fields["moves"] = Value.ForList([.. moves.Select(Value.ForString)]);
        matchStruct.Fields["fen_history"] = Value.ForList([.. fenHistory.Select(Value.ForString)]);

        GetResponse response = new() { Record = matchStruct };
        DbClient
            .GetAsync(
                Arg.Is<GetRequest>(r => r.Collection == "matches" && r.Id == matchId),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns(GrpcHelper.GrpcCall(response));
    }

    internal void SetupUserMatches(
        string userId,
        IReadOnlyList<UserMatchFixture> whiteMatches,
        IReadOnlyList<UserMatchFixture> blackMatches)
    {
        SetupListMatchesByField("white_user_id", userId, whiteMatches);
        SetupListMatchesByField("black_user_id", userId, blackMatches);
    }

    private void SetupListMatchesByField(string field, string userId, IReadOnlyList<UserMatchFixture> matches)
    {
        ListResponse response = new();
        foreach (UserMatchFixture m in matches)
        {
            response.Records.Add(BuildMatchRecord(m));
        }

        DbClient
            .ListAsync(
                Arg.Is<ListRequest>(r =>
                    r.Collection == "matches" &&
                    r.Filter.Fields.ContainsKey(field) &&
                    r.Filter.Fields[field].StringValue == userId),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns(GrpcHelper.GrpcCall(response));
    }

    private static Struct BuildMatchRecord(UserMatchFixture m)
    {
        Struct s = new();
        s.Fields["id"] = Value.ForString(m.Id);
        s.Fields["status"] = Value.ForString(m.Status);
        s.Fields["white_user_id"] = m.WhiteUserId is not null ? Value.ForString(m.WhiteUserId) : Value.ForNull();
        s.Fields["black_user_id"] = m.BlackUserId is not null ? Value.ForString(m.BlackUserId) : Value.ForNull();
        s.Fields["white_bot_id"] = m.WhiteBotId is not null ? Value.ForString(m.WhiteBotId) : Value.ForNull();
        s.Fields["black_bot_id"] = m.BlackBotId is not null ? Value.ForString(m.BlackBotId) : Value.ForNull();
        s.Fields["time_format_id"] = Value.ForString(m.TimeFormatId);
        s.Fields["time_format_base_ms"] = Value.ForNumber(m.BaseMs);
        s.Fields["time_format_increment_ms"] = Value.ForNumber(m.IncrementMs);
        s.Fields["time_format_category"] = Value.ForString(m.Category);
        s.Fields["last_move_at"] = Value.ForString(m.LastMoveAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        s.Fields["moves"] = Value.ForList([.. m.Moves.Select(Value.ForString)]);
        return s;
    }

    internal void SetupMatchNotFound(string matchId)
    {
        DbClient
            .GetAsync(
                Arg.Is<GetRequest>(r => r.Collection == "matches" && r.Id == matchId),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns<AsyncUnaryCall<GetResponse>>(_ =>
                throw new RpcException(new Status(StatusCode.NotFound, "not found")));
    }

    internal void SetupUserResolution(string userId, string username)
    {
        GetUserResponse response = new() { User = new User { Id = userId, Username = username } };
        UsersClient
            .GetUserAsync(
                Arg.Is<GetUserRequest>(r => r.UserId == userId),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns(GrpcHelper.GrpcCall(response));
    }

    private void SetupDefaultUserResolution()
    {
        GetUserResponse fallback = new() { User = new User { Id = "unknown", Username = "Unknown" } };
        UsersClient
            .GetUserAsync(
                Arg.Any<GetUserRequest>(),
                Arg.Any<Metadata>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>())
            .Returns(GrpcHelper.GrpcCall(fallback));
    }

    private void SetupDefaultBotResolution()
    {
        ListBotsResponse response = new();
        response.Bots.Add(new Bot { Id = "stockfish-3", Name = "Stockfish Level 3", Elo = 1400 });
        BotsClient
            .ListBotsAsync(
                Arg.Any<ListBotsRequest>(),
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
            StartingFen: "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1",
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
