namespace FlowDesk.Core.QuickLook;

/// <summary>Bounds both the caller's wait and outstanding work. Cancellation/timeout does not imply
/// that a native call stopped. Only the actual callback's finally releases the single-flight lease.</summary>
public sealed class SingleFlightPreviewQuery : IDisposable
{
    private int busy, disposed;
    public async Task<T?> RunAsync<T>(Func<T?> operation, Func<Action, bool> schedule,
        TimeSpan timeout, CancellationToken token) where T : class
    {
        if (Volatile.Read(ref disposed) != 0 || Interlocked.CompareExchange(ref busy, 1, 0) != 0) return null;
        var completion = new TaskCompletionSource<T?>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            if (!schedule(() =>
            {
                try
                {
                    completion.TrySetResult(token.IsCancellationRequested || Volatile.Read(ref disposed) != 0 ? null : operation());
                }
                catch (Exception) { completion.TrySetResult(null); }
                finally { Volatile.Write(ref busy, 0); }
            })) { Volatile.Write(ref busy, 0); return null; }
        }
        catch (Exception) { Volatile.Write(ref busy, 0); return null; }
        try { return await completion.Task.WaitAsync(timeout, token).ConfigureAwait(false); }
        catch (TimeoutException) { return null; }
    }
    public void Dispose() => Interlocked.Exchange(ref disposed, 1);
}
