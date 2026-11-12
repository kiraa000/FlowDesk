namespace FlowDesk.Core.Hotkeys;

/// <summary>Short waits inside actions. Returns false when the feature was disabled meanwhile.</summary>
public interface IActionDelay
{
    bool Wait(TimeSpan duration, CancellationToken token);
}

/// <summary>Waits on the calling thread but wakes immediately when the host is stopping.</summary>
public sealed class CancellableDelay : IActionDelay
{
    public bool Wait(TimeSpan duration, CancellationToken token)
    {
        if (token.IsCancellationRequested) return false;
        if (duration <= TimeSpan.Zero) return true;
        return !token.WaitHandle.WaitOne(duration);
    }
}
