namespace FlowDesk.Core.Hotkeys;

public readonly record struct OwnedHotkeyEvent(bool Swallow, bool Armed);

/// <summary>Hook-thread only. Forward every down so Windows can produce WM_HOTKEY (and foreground
/// permission); suppress only the paired up. Only registered bindings may arm a release.</summary>
public sealed class HotkeyInputOwnership
{
    public const long TimeoutMilliseconds = 5000;
    private readonly Dictionary<uint, (long Started, HotkeyBinding Binding)> owned = [];
    private long? maskAltSince;
    public OwnedHotkeyEvent Observe(uint key, bool down, long now, HotkeyBinding? registered = null)
    {
        if (owned.Count != 0)
            foreach (var stale in owned.Where(p => now < p.Value.Started || now - p.Value.Started >= TimeoutMilliseconds).Select(p => p.Key).ToArray()) owned.Remove(stale);
        if (owned.ContainsKey(key))
        {
            if (!down) owned.Remove(key);
            return new(!down, false); // repeat downs must also reach RegisterHotKey
        }
        if (!down || registered is null || registered.VirtualKey != key || KeyChord.IsModifier(key)) return default;
        owned[key] = (now, registered);
        if ((registered.Modifiers & HotkeyCatalog.ModAlt) != 0) maskAltSince = now;
        return new(false, true);
    }
    public bool TakeAltMask(uint key, bool down, long now)
    {
        if (maskAltSince is not long started) return false;
        if (now < started || now - started >= TimeoutMilliseconds) { maskAltSince = null; return false; }
        if (down || key is not (0x12 or 0xA4 or 0xA5)) return false;
        maskAltSince = null;
        return true;
    }
    public void Reset() { owned.Clear(); maskAltSince = null; }
}

/// <summary>Non-invasive observation until a qualifying Win-up; never consumes Win-down or other chords.</summary>
public sealed class LoneWindowsKey
{
    public const long MaxHoldMilliseconds = 500;
    private readonly HashSet<int> held = [];
    private int candidate;
    private long started;
    private bool mouseDown;
    public bool IsCandidate => candidate != 0;
    public void Reset() { held.Clear(); candidate = 0; mouseDown = false; }
    public void ObserveMouse(bool buttonDown, bool buttonUp, bool wheel)
    {
        if (buttonDown || wheel) candidate = 0;
        if (buttonDown) mouseDown = true;
        if (buttonUp) mouseDown = false;
    }
    public bool ObserveKey(int key, bool down, long now, bool enabled, bool otherInputHeld = false)
    {
        bool wasHeld = held.Contains(key);
        if (down)
        {
            if (!wasHeld)
            {
                if (enabled && key is 0x5B or 0x5C && held.Count == 0 && !mouseDown && !otherInputHeld)
                { candidate = key; started = now; }
                else candidate = 0;
                held.Add(key);
            }
            if (!enabled) candidate = 0;
            return false;
        }
        held.Remove(key);
        bool tap = enabled && candidate == key && wasHeld && now >= started && now - started < MaxHoldMilliseconds;
        if (candidate == key || !enabled) candidate = 0;
        return tap;
    }
}
