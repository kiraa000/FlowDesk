namespace FlowDesk.Core.Hotkeys;

public readonly record struct ShortcutKeyStroke(ushort VirtualKey, bool KeyUp, bool Extended = false)
{
    public nuint ExtraInfo => ShortcutKeyboardState.InjectionTag;
}

public readonly record struct WinReleaseResult(bool Attempted, bool Swallow, uint Inserted, uint CleanupInserted, bool Failed)
{
    public bool MaskInjected => Inserted >= 2;
}

/// <summary>Fail-open replacement transaction. The original up is suppressed ONLY after a batch containing
/// its replacement up was inserted in full. No deferred release debt survives exceptions, stop, or unhook.</summary>
public static class WinKeyRelease
{
    public static WinReleaseResult Process(ushort winKey, bool acceptedGesture, bool stopping,
        Func<ShortcutKeyStroke[], uint> send)
    {
        if (!acceptedGesture || stopping || winKey is not (0x5B or 0x5C)) return default;
        uint inserted = 0, cleanup = 0;
        bool complete = false;
        try
        {
            inserted = send([new(0xE8, false), new(0xE8, true), new(winKey, true, Extended: true)]);
            complete = inserted == 3;
        }
        catch (Exception) { /* caller forwards the original Win-up */ }
        finally
        {
            if (!complete)
            {
                // A partial batch (or a sender throwing after insertion) might leave vkE8 down.
                // Win-up itself is NOT retried: the physical event is still available to forward.
                try { cleanup = send([new(0xE8, true)]); } catch (Exception) { }
            }
        }
        return new(true, complete, inserted, cleanup, !complete);
    }
}
