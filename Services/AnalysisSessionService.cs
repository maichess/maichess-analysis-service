using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Maichess.Engine.V1;
using Maichess.MoveValidator.V1;
using MaichessAnalysisService.Domain;
using Microsoft.Extensions.Options;
using SocketGrpc = Socket.V1.Socket;

namespace MaichessAnalysisService.Services;

internal sealed class AnalysisSessionService(
    IAnalysisGameRepository gameRepo,
    IAnalysisResultRepository resultRepo,
    Bots.BotsClient botsClient,
    Moves.MovesClient movesClient,
    SocketGrpc.SocketClient socketClient,
    IOptions<AnalysisConfig> configOptions)
{
    private readonly AnalysisConfig config = configOptions.Value;
    private readonly ConcurrentDictionary<string, AnalysisSession> sessions =
        new(StringComparer.Ordinal);

    internal async Task<AnalysisSession> CreateSessionAsync(
        string userId, string gameId, string botId, int lineCount, CancellationToken ct)
    {
        AnalysisGame game = await gameRepo.GetByIdAsync(gameId, ct)
            ?? throw new AnalysisGameNotFoundException();

        if (game.UserId != userId)
        {
            throw new AccessDeniedException();
        }

        if (sessions.TryRemove(userId, out AnalysisSession? existing))
        {
            CancelAnalysis(existing);
        }

        string sessionId = $"s-{Guid.NewGuid():N}"[..10];
        AnalysisSession session = new(sessionId, userId, gameId, botId, lineCount, game);
        sessions[userId] = session;
        return session;
    }

    internal Task DestroySessionAsync(string sessionId, string userId, CancellationToken ct)
    {
        AnalysisSession session = GetSession(sessionId, userId);
        sessions.TryRemove(userId, out _);
        CancelAnalysis(session);
        return Task.CompletedTask;
    }

    internal Task<(int Index, string Fen)> NavigateAsync(
        string sessionId, string userId, int index, CancellationToken ct)
    {
        AnalysisSession session = GetSession(sessionId, userId);

        if (index < 0 || index > session.Game.Moves.Count)
        {
            throw new NavigationOutOfRangeException();
        }

        session.CurrentIndex = index;
        session.WhatifMoves.Clear();
        session.WhatifFens.Clear();
        RestartAnalysis(session);

        return Task.FromResult((session.CurrentIndex, session.GetCurrentFen()));
    }

    internal async Task<(int WhatifIndex, string Fen)> PlayWhatifAsync(
        string sessionId, string userId, string uciMove, CancellationToken ct)
    {
        AnalysisSession session = GetSession(sessionId, userId);

        string currentFen = session.GetCurrentFen();
        ValidateMoveResponse resp = await movesClient.ValidateMoveAsync(
            new ValidateMoveRequest { Fen = currentFen, Move = uciMove },
            cancellationToken: ct);

        if (!resp.Valid)
        {
            throw new InvalidWhatifMoveException(resp.Reason);
        }

        session.WhatifMoves.Add(uciMove);
        session.WhatifFens.Add(resp.ResultingFen);
        RestartAnalysis(session);

        return (session.WhatifMoves.Count, session.GetCurrentFen());
    }

    internal Task<(int Index, string Fen)> ResetWhatifAsync(
        string sessionId, string userId, CancellationToken ct)
    {
        AnalysisSession session = GetSession(sessionId, userId);

        session.WhatifMoves.Clear();
        session.WhatifFens.Clear();
        RestartAnalysis(session);

        return Task.FromResult((session.CurrentIndex, session.GetCurrentFen()));
    }

    internal Task<(int WhatifIndex, string Fen)> UndoLastWhatifAsync(
        string sessionId, string userId, CancellationToken ct)
    {
        AnalysisSession session = GetSession(sessionId, userId);

        if (session.WhatifMoves.Count == 0)
        {
            throw new WhatifEmptyException();
        }

        session.WhatifMoves.RemoveAt(session.WhatifMoves.Count - 1);
        session.WhatifFens.RemoveAt(session.WhatifFens.Count - 1);
        RestartAnalysis(session);

        return Task.FromResult((session.WhatifMoves.Count, session.GetCurrentFen()));
    }

    internal async Task<string> GetWhatifPgnAsync(
        string sessionId, string userId, CancellationToken ct)
    {
        AnalysisSession session = GetSession(sessionId, userId);

        if (session.WhatifMoves.Count == 0)
        {
            throw new WhatifEmptyException();
        }

        string whatifBaseFen = session.GetBaseFen();
        ConvertSequenceToSanRequest req = new() { StartingFen = whatifBaseFen };
        req.UciMoves.AddRange(session.WhatifMoves);
        ConvertSequenceToSanResponse resp = await movesClient.ConvertSequenceToSanAsync(
            req, cancellationToken: ct);

        return BuildWhatifPgn(whatifBaseFen, resp.SanMoves);
    }

    internal Task StartAnalysisAsync(
        string sessionId, string userId, string? botIdOverride, int? lineCountOverride, CancellationToken ct)
    {
        AnalysisSession session = GetSession(sessionId, userId);

        if (botIdOverride is not null)
        {
            session.BotId = botIdOverride;
        }

        if (lineCountOverride.HasValue)
        {
            session.LineCount = lineCountOverride.Value;
        }

        CancelAnalysis(session);
        StartAnalysis(session);
        return Task.CompletedTask;
    }

    internal Task StopAnalysisAsync(string sessionId, string userId, CancellationToken ct)
    {
        AnalysisSession session = GetSession(sessionId, userId);
        CancelAnalysis(session);
        return Task.CompletedTask;
    }

    internal async Task RunAnalysisStreamAsync(AnalysisSession session, CancellationToken ct)
    {
        string currentFen = session.GetCurrentFen();
        string botId = session.BotId;
        int lineCount = session.LineCount;
        string sessionId = session.Id;
        string userId = session.UserId;

        try
        {
            IReadOnlyList<AnalysisResultRecord> cached =
                await resultRepo.GetCachedDepthsAsync(currentFen, botId, ct);

            IReadOnlyList<AnalysisResultRecord> filtered = [.. cached
                .Where(r => r.LineCount >= lineCount)
                .OrderBy(r => r.Depth)];

            int maxCachedDepth = filtered.Count > 0 ? filtered.Max(r => r.Depth) : 0;

            foreach (AnalysisResultRecord record in filtered)
            {
                await EmitAnalysisUpdateAsync(
                    userId,
                    sessionId,
                    (uint)record.Depth,
                    [.. record.Lines.Take(lineCount)],
                    ct);
            }

            using global::Grpc.Core.AsyncServerStreamingCall<AnalysisUpdate> engineCall =
                botsClient.AnalyzePosition(
                    new AnalyzePositionRequest
                    {
                        Fen = currentFen,
                        BotId = botId,
                        LineCount = (uint)lineCount,
                    },
                    cancellationToken: ct);

            uint finalDepth = 0;
            await foreach (AnalysisUpdate update in
                engineCall.ResponseStream.ReadAllAsync(ct))
            {
                if (update.Depth <= maxCachedDepth)
                {
                    continue;
                }

                finalDepth = update.Depth;

                IReadOnlyList<AnalysisLine> lines = [.. update.Lines.Select(pv =>
                    new AnalysisLine((int)pv.Rank, pv.EvaluationCp, [.. pv.Moves]))];

                if (botId == config.DefaultBotId && lineCount == config.DefaultLineCount)
                {
                    await resultRepo.InsertDepthAsync(
                        new AnalysisResultRecord(
                            Id: string.Empty,
                            Fen: currentFen,
                            BotId: botId,
                            LineCount: lineCount,
                            Depth: (int)update.Depth,
                            Lines: lines,
                            CreatedAt: DateTimeOffset.UtcNow),
                        ct);
                }

                await EmitAnalysisUpdateAsync(userId, sessionId, update.Depth, lines, ct);
            }

            await EmitAnalysisCompleteAsync(userId, sessionId, finalDepth, ct);
        }
        catch (OperationCanceledException)
        {
        }
#pragma warning disable CA1031
        catch (Exception ex)
#pragma warning restore CA1031
        {
            await EmitAnalysisErrorAsync(userId, sessionId, ex.Message, CancellationToken.None);
        }
    }

    private static void CancelAnalysis(AnalysisSession session)
    {
        session.ActiveCts?.Cancel();
        session.ActiveCts?.Dispose();
        session.ActiveCts = null;
    }

    private static string BuildWhatifPgn(string baseFen, IEnumerable<string> sanMoves)
    {
        string[] fenParts = baseFen.Split(' ');
        bool isWhiteToMove = fenParts.Length < 2 || fenParts[1] == "w";
        int moveNumber = fenParts.Length >= 6
            ? int.Parse(fenParts[5], CultureInfo.InvariantCulture)
            : 1;

        StringBuilder sb = new();
        sb.AppendLine($"[FEN \"{baseFen}\"]");
        sb.AppendLine("[SetUp \"1\"]");
        sb.AppendLine();

        bool sideIsWhite = isWhiteToMove;
        int currentNumber = moveNumber;
        int i = 0;

        foreach (string san in sanMoves)
        {
            if (sideIsWhite)
            {
                sb.Append(CultureInfo.InvariantCulture, $"{currentNumber}. ");
            }
            else if (i == 0)
            {
                sb.Append(CultureInfo.InvariantCulture, $"{currentNumber}... ");
            }

            sb.Append(san);
            sb.Append(' ');

            if (!sideIsWhite)
            {
                currentNumber++;
            }

            sideIsWhite = !sideIsWhite;
            i++;
        }

        sb.Append('*');
        return sb.ToString().TrimEnd();
    }

    private AnalysisSession GetSession(string sessionId, string userId) =>
        sessions.TryGetValue(userId, out AnalysisSession? session) && session.Id == sessionId
            ? session
            : throw new SessionNotFoundException();

    private void StartAnalysis(AnalysisSession session)
    {
        CancellationTokenSource cts = new();
        session.ActiveCts = cts;
        _ = Task.Run(() => RunAnalysisStreamAsync(session, cts.Token));
    }

    private void RestartAnalysis(AnalysisSession session)
    {
        CancelAnalysis(session);
        StartAnalysis(session);
    }

    private async Task EmitAnalysisUpdateAsync(
        string userId,
        string sessionId,
        uint depth,
        IReadOnlyList<AnalysisLine> lines,
        CancellationToken ct)
    {
        Value[] lineValues = [.. lines.Select(l => Value.ForStruct(new Struct
        {
            Fields =
            {
                ["rank"] = Value.ForNumber(l.Rank),
                ["evaluation_cp"] = Value.ForNumber(l.EvaluationCp),
                ["moves"] = Value.ForList([.. l.Moves.Select(Value.ForString)]),
            },
        }))];

        await socketClient.EmitEventAsync(
            new Socket.V1.EmitEventRequest
            {
                UserId = userId,
                Event = "analysis_update",
                Payload = new Struct
                {
                    Fields =
                    {
                        ["session_id"] = Value.ForString(sessionId),
                        ["depth"] = Value.ForNumber(depth),
                        ["lines"] = Value.ForList(lineValues),
                    },
                },
            },
            cancellationToken: ct);
    }

    private async Task EmitAnalysisCompleteAsync(
        string userId, string sessionId, uint finalDepth, CancellationToken ct)
    {
        await socketClient.EmitEventAsync(
            new Socket.V1.EmitEventRequest
            {
                UserId = userId,
                Event = "analysis_complete",
                Payload = new Struct
                {
                    Fields =
                    {
                        ["session_id"] = Value.ForString(sessionId),
                        ["final_depth"] = Value.ForNumber(finalDepth),
                    },
                },
            },
            cancellationToken: ct);
    }

    private async Task EmitAnalysisErrorAsync(
        string userId, string sessionId, string message, CancellationToken ct)
    {
        await socketClient.EmitEventAsync(
            new Socket.V1.EmitEventRequest
            {
                UserId = userId,
                Event = "analysis_error",
                Payload = new Struct
                {
                    Fields =
                    {
                        ["session_id"] = Value.ForString(sessionId),
                        ["message"] = Value.ForString(message),
                    },
                },
            },
            cancellationToken: ct);
    }
}
