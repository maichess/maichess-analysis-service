using Grpc.Core;
using Maichess.Analysis.V1;
using Maichess.Engine.V1;

namespace MaichessAnalysisService.Grpc;

internal sealed class AnalysisGrpcService(Bots.BotsClient botsClient)
    : Analysis.AnalysisBase
{
    public override async Task StreamPositionAnalysis(
        StreamPositionAnalysisRequest request,
        IServerStreamWriter<PositionAnalysisUpdate> responseStream,
        ServerCallContext context)
    {
        using global::Grpc.Core.AsyncServerStreamingCall<AnalysisUpdate> engineCall = botsClient.AnalyzePosition(
            new AnalyzePositionRequest
            {
                Fen = request.Fen,
                BotId = request.BotId,
                LineCount = request.LineCount,
            },
            cancellationToken: context.CancellationToken);

        await foreach (AnalysisUpdate update in
            engineCall.ResponseStream.ReadAllAsync(context.CancellationToken))
        {
            PositionAnalysisUpdate relayed = new() { Depth = update.Depth };
            relayed.Lines.AddRange(update.Lines.Select(pv => new AnalysisLine
            {
                Rank = pv.Rank,
                EvaluationCp = pv.EvaluationCp,
                Moves = { pv.Moves },
            }));
            await responseStream.WriteAsync(relayed, context.CancellationToken);
        }
    }
}
