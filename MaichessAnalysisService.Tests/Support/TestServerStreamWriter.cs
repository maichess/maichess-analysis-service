using Grpc.Core;

namespace MaichessAnalysisService.Tests.Support;

internal sealed class TestServerStreamWriter<T> : IServerStreamWriter<T>
{
    private readonly List<T> written = [];

    internal IReadOnlyList<T> Written => written;

    public WriteOptions? WriteOptions { get; set; }

    public Task WriteAsync(T message)
    {
        written.Add(message);
        return Task.CompletedTask;
    }

    public Task WriteAsync(T message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        written.Add(message);
        return Task.CompletedTask;
    }
}
