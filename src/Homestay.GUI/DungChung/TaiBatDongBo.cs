namespace Homestay.GUI.Common;

// Called on the UI thread. A superseded request may ignore cancellation;
// the version check still prevents it from applying its old result.
public sealed class AsyncLoader : IDisposable
{
    private long version;
    private CancellationTokenSource? current;
    private bool disposed;
    public async Task LoadAsync<T>(Func<CancellationToken, Task<T>> load, Action<T> apply)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var request = Interlocked.Increment(ref version);
        current?.Cancel();
        using var source = new CancellationTokenSource();
        current = source;
        try
        {
            var result = await load(source.Token);
            if (!disposed && request == Volatile.Read(ref version) && !source.IsCancellationRequested)
                apply(result);
        }
        catch (OperationCanceledException) when (source.IsCancellationRequested) { }
        catch (Exception) when (disposed || request != Volatile.Read(ref version)) { }
        finally { if (ReferenceEquals(current, source)) current = null; }
    }
    public void CancelPending()
    {
        Interlocked.Increment(ref version);
        current?.Cancel();
        current = null;
    }
    public void Dispose()
    {
        disposed = true;
        CancelPending();
    }
}
