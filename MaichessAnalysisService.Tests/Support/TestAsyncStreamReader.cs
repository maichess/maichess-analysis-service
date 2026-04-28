using Grpc.Core;

namespace MaichessAnalysisService.Tests.Support;

internal sealed class TestAsyncStreamReader<T> : IAsyncStreamReader<T>
{
    private readonly Queue<T> items;

    internal TestAsyncStreamReader(IEnumerable<T> items)
    {
        this.items = new Queue<T>(items);
    }

    public T Current { get; private set; } = default!;

    public Task<bool> MoveNext(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (items.Count == 0)
        {
            return Task.FromResult(false);
        }

        Current = items.Dequeue();
        return Task.FromResult(true);
    }
}
