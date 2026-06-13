using Grpc.Core;

namespace MaichessAnalysisService.Tests.Support;

internal static class GrpcHelper
{
    internal static AsyncUnaryCall<T> GrpcCall<T>(T response) =>
        new(
            Task.FromResult(response),
            Task.FromResult(Metadata.Empty),
            () => Status.DefaultSuccess,
            () => Metadata.Empty,
            () => { });

    internal static AsyncServerStreamingCall<T> ServerStream<T>(IEnumerable<T> items) =>
        new(
            new TestAsyncStreamReader<T>(items),
            Task.FromResult(Metadata.Empty),
            () => Status.DefaultSuccess,
            () => Metadata.Empty,
            () => { });
}
