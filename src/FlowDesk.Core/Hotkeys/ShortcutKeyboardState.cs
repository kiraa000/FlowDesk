namespace FlowDesk.Core.Hotkeys;

public readonly record struct ShortcutModifiers(bool Win, bool Alt, bool Shift, bool Control);
public readonly record struct ShortcutKeyObservation(bool OwnInput, bool ResetSearch, bool WinKeyUp,
    bool GestureAccepted, long HoldId, int Repeats);

/// <summary>Hook-thread-owned state, not asynchronous OS state (which changes AFTER the keyboard callback).
/// Other applications' injected modifiers count; only our tagged replacement events are excluded.</summary>
public sealed class ShortcutKeyboardState
{
    public const uint InjectionTag = 0x46444B32; // FD K2, fits ULONG_PTR on both architectures
    private readonly bool[] _down = new bool[256];
    private bool _gestureAccepted;
    private readonly long[] _holds = new long[2];
    private readonly int[] _repeats = new int[2];
    private long _nextHold;

    public ShortcutModifiers Modifiers => new(_down[0x5B] || _down[0x5C],
        _down[0xA4] || _down[0xA5], _down[0xA0] || _down[0xA1], _down[0xA2] || _down[0xA3]);
    public long LeftHoldId => _holds[0];
    public long RightHoldId => _holds[1];

    /// <summary>Once before installing the hook, to include modifiers held while shortcuts were enabled.</summary>
    public void Seed(Func<int, bool> isDown)
    {
        foreach (int key in new[] { 0x5B, 0x5C, 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5 })
            Observe(key, isDown(key), 0);
    }

    public void AcceptWinGesture()
    {
        if (Modifiers.Win) _gestureAccepted = true;
    }

    public ShortcutKeyObservation Observe(int key, bool down, nuint extraInfo, uint scanCode = 0, bool extended = false)
    {
        if (extraInfo == InjectionTag) return new(OwnInput: true, false, false, false, 0, 0);
        key = key switch { 0x10 => scanCode == 0x36 ? 0xA1 : 0xA0,
            0x11 => extended ? 0xA3 : 0xA2, 0x12 => extended ? 0xA5 : 0xA4, _ => key };
        if (key is not (0x5B or 0x5C or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5)) return default;
        bool wasDown = _down[key];
        bool anyWinWasDown = Modifiers.Win;
        _down[key] = down;
        if (key is 0x5B or 0x5C)
        {
            int index = key - 0x5B;
            if (down)
            {
                if (!wasDown)
                {
                    _holds[index] = ++_nextHold; _repeats[index] = 0;
                    if (!anyWinWasDown) _gestureAccepted = false;
                }
                else _repeats[index]++;
                return default;
            }
            bool accepted = wasDown && _gestureAccepted;
            if (!Modifiers.Win) _gestureAccepted = false;
            return new(false, !Modifiers.Win, true, accepted, _holds[index], _repeats[index]);
        }
        return new(false, !down && key is (0xA4 or 0xA5) && !Modifiers.Alt, false, false, 0, 0);
    }
}
