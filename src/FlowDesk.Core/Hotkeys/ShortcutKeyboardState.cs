namespace FlowDesk.Core.Hotkeys;

public readonly record struct ShortcutModifiers(bool Win, bool Alt, bool Shift, bool Control);
public readonly record struct ShortcutKeyObservation(bool OwnInput, bool ResetSearch, bool WinKeyUp,
    bool GestureAccepted, long HoldId, int Repeats);

/// <summary>Hook-thread-owned state, not asynchronous OS state (which changes AFTER the keyboard callback).
/// Other applications' injected modifiers count; only our tagged replacement events are excluded.</summary>
public sealed class ShortcutKeyboardState
{
    public const uint InjectionTag = 0x46444B32; // FD K2, fits ULONG_PTR on both architectures
    public const int StaleDownGraceMilliseconds = 1000;
    private static readonly int[] ModifierKeys = [0x5B, 0x5C, 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5];
    private readonly Func<long> _clock;
    private readonly bool[] _down = new bool[256];
    private readonly long[] _downSince = new long[256];
    private bool _gestureAccepted;
    private readonly long[] _holds = new long[2];
    private readonly int[] _repeats = new int[2];
    private long _nextHold;

    public ShortcutKeyboardState(Func<long>? clock = null) => _clock = clock ?? (() => Environment.TickCount64);

    public ShortcutModifiers Modifiers => new(_down[0x5B] || _down[0x5C],
        _down[0xA4] || _down[0xA5], _down[0xA0] || _down[0xA1], _down[0xA2] || _down[0xA3]);
    public long LeftHoldId => _holds[0];
    public long RightHoldId => _holds[1];

    /// <summary>Startup/session boundary snapshot. Discard the old gesture arm even if Win is still held.
    /// Sampling completes before mutation, so a failed sampler cannot leave a half-reseeded state.</summary>
    public void Seed(Func<int, bool> isDown)
    {
        var snapshot = ModifierKeys.Select(isDown).ToArray();
        Array.Clear(_down);
        Array.Clear(_downSince);
        Array.Clear(_holds);
        Array.Clear(_repeats);
        _gestureAccepted = false;
        for (int i = 0; i < ModifierKeys.Length; i++) Observe(ModifierKeys[i], snapshot[i], 0);
    }

    public bool ReseedForSessionChange(uint reason, Func<int, bool> isDown)
    {
        // WTS console/remote connect/disconnect, logon/logoff, lock/unlock; user desktop ready.
        if (reason is not (>= 1 and <= 8 or 0xF)) return false;
        Seed(isDown);
        return true;
    }

    /// <summary>Called only on wheel events, outside the keyboard callback. Hook-only downs get a 1s
    /// grace period; repeats do not extend it. This clears state, never synthesizes a key-up.</summary>
    public bool ReconcileStale(Func<int, bool> isDown)
    {
        long now = _clock();
        int clear = 0;
        for (int i = 0; i < ModifierKeys.Length; i++)
        {
            int key = ModifierKeys[i];
            if (_down[key] && now - _downSince[key] >= StaleDownGraceMilliseconds && !isDown(key))
                clear |= 1 << i;
        }
        for (int i = 0; i < ModifierKeys.Length; i++)
        {
            if ((clear & (1 << i)) == 0) continue;
            _down[ModifierKeys[i]] = false;
            _downSince[ModifierKeys[i]] = 0;
        }
        if (!Modifiers.Win) _gestureAccepted = false;
        return clear != 0;
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
        if (down && !wasDown) _downSince[key] = _clock();
        else if (!down) _downSince[key] = 0;
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
