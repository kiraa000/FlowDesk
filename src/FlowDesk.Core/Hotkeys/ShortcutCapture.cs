namespace FlowDesk.Core.Hotkeys;

/// <summary>Physical held-key state, independent of WinUI and keyboard layout. Releasing a chord
/// preserves the candidate while held caps become empty. Multiple action keys are rejected.</summary>
public sealed class ShortcutCapture
{
    private readonly HashSet<uint> held = [];
    public KeyChord? Candidate { get; private set; }
    public bool MultipleKeys { get; private set; }
    public KeyChord Held => new(held.Aggregate(0u, (mods, key) => mods | KeyChord.ModifierFor(key)), held.FirstOrDefault(k => !KeyChord.IsModifier(k)));
    public void Observe(uint key, bool down)
    {
        if (down) held.Add(key); else held.Remove(key);
        MultipleKeys = held.Count(k => !KeyChord.IsModifier(k)) > 1;
        if (!down) return;
        Candidate = MultipleKeys ? null : Held;
    }
    public void ResetHeld() { held.Clear(); MultipleKeys = false; }
}
