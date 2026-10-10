namespace Suiyi.Core;

public readonly record struct RequestLease(long Generation, CancellationToken Token);

/// <summary>Isolates requests even when a remote service ignores cancellation.</summary>
public sealed class LatestRequestGate : IDisposable
{
    private readonly object _sync = new();
    private CancellationTokenSource? _source;
    private long _generation;
    private bool _disposed;

    public RequestLease Begin(CancellationToken cancellationToken = default)
    {
        CancellationTokenSource? previous;
        RequestLease lease;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            previous = _source;
            _source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            lease = new(++_generation, _source.Token);
        }
        CancelAndDispose(previous);
        return lease;
    }

    public bool IsCurrent(RequestLease lease)
    {
        lock (_sync)
            return !_disposed && _source is not null && lease.Generation == _generation &&
                !lease.Token.IsCancellationRequested;
    }

    public bool TryCommit(RequestLease lease, Action commit)
    {
        ArgumentNullException.ThrowIfNull(commit);
        lock (_sync)
        {
            if (_disposed || _source is null || lease.Generation != _generation || lease.Token.IsCancellationRequested)
                return false;
            commit();
            return true;
        }
    }

    public void Cancel()
    {
        CancellationTokenSource? previous;
        lock (_sync)
        {
            ++_generation;
            previous = _source;
            _source = null;
        }
        CancelAndDispose(previous);
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
        }
        Cancel();
    }

    private static void CancelAndDispose(CancellationTokenSource? source)
    {
        if (source is null) return;
        try { source.Cancel(); }
        finally { source.Dispose(); }
    }
}
