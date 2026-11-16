namespace FlowDesk.Core.QuickLook;

/// <summary>Independent selection and thumbnail admission, sharing one serial native scheduler.
/// At most one callback per kind (two total) may be outstanding, including timed-out/cancelled work.
/// A poll cannot reject a thumbnail; FIFO scheduling puts it ahead of later polls. Each kind retains
/// SingleFlightPreviewQuery's lease until its actual callback finally completes, never until timeout.</summary>
public sealed class ShellPreviewQueries(Func<Action, bool> schedule) : IDisposable
{
    public const int QueueCapacity = 2;
    private readonly SingleFlightPreviewQuery reads = new();
    private readonly SingleFlightPreviewQuery thumbnails = new();

    public Task<T?> ReadAsync<T>(Func<T?> operation, TimeSpan timeout, CancellationToken token) where T : class =>
        reads.RunAsync(operation, schedule, timeout, token);

    public Task<T?> ThumbnailAsync<T>(Func<T?> operation, TimeSpan timeout, CancellationToken token) where T : class =>
        thumbnails.RunAsync(operation, schedule, timeout, token);

    public void Dispose() { reads.Dispose(); thumbnails.Dispose(); }
}
