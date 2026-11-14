namespace FlowDesk.App.Services;

/// <summary>
/// Coalesces redirected launches onto the UI dispatcher, including launches received before the
/// window exists. The target is the same BringForward action used by the tray, not another window.
/// </summary>
public sealed class ActivationRouter(Func<Action, bool> enqueue) : IDisposable
{
    private readonly object gate = new();
    private Action? target;
    private bool pending, queued, disposed;

    public void Request()
    {
        lock (gate)
        {
            if (disposed) return;
            pending = true;
        }
        Schedule();
    }

    public void SetTarget(Action bringForward)
    {
        ArgumentNullException.ThrowIfNull(bringForward);
        lock (gate)
        {
            if (disposed) return;
            target = bringForward;
        }
        Schedule();
    }

    private void Schedule()
    {
        lock (gate)
        {
            if (disposed || queued || !pending || target is null) return;
            queued = true;
        }
        bool accepted = false;
        try { accepted = enqueue(Drain); }
        finally
        {
            // A shutting-down dispatcher can reject the callback. Retain the request for a later
            // Request/SetTarget instead of falsely considering it delivered.
            if (!accepted) lock (gate) queued = false;
        }
    }

    private void Drain()
    {
        Action? action;
        lock (gate)
        {
            queued = false;
            if (disposed || !pending || target is null) return;
            pending = false;
            action = target;
        }
        action();
    }

    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
            pending = queued = false;
            target = null;
        }
    }
}
