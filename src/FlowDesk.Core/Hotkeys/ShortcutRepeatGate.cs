namespace FlowDesk.Core.Hotkeys;

/// <summary>When a host has a keyboard hook, all registrations can allow native repeats and keep
/// this action-level rule instead. Chord ownership may then transfer during a swap without inheriting
/// the previous action's MOD_NOREPEAT setting. Message-thread owned; no timers or synthetic input.</summary>
public sealed class ShortcutRepeatGate
{
    private readonly HashSet<uint> down = [];
    private readonly Dictionary<uint, long> holds = [];
    private readonly Dictionary<string, long> accepted = new(StringComparer.Ordinal);
    private long sequence;
    public void Observe(uint key, bool isDown)
    {
        if (isDown) { if (down.Add(key)) holds[key] = ++sequence; }
        else down.Remove(key);
    }
    public bool Accept(HotkeyBinding binding)
    {
        if (binding.Repeat) return true;
        long hold = holds.GetValueOrDefault(binding.VirtualKey);
        if (accepted.TryGetValue(binding.Id, out long previous) && previous == hold) return false;
        accepted[binding.Id] = hold; return true;
    }
    public void Reset() { down.Clear(); holds.Clear(); accepted.Clear(); }
}
