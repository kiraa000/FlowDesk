namespace FlowDesk.Core.Hotkeys;

public enum WheelGesture { None, Resize, Desktop, MoveDesktop, Search }

public readonly record struct WheelDecision(WheelGesture Gesture, bool Accepted, bool MaskMenu);

/// <summary>Message-thread-only input policy. Suppression includes throttled events, not unrelated modifiers.</summary>
public sealed class WheelGestures
{
    private bool _winMasked;
    private bool _altMasked;
    private readonly Dictionary<WheelGesture, long> _last = [];
    private readonly Dictionary<WheelGesture, (long Sum, long Timestamp, int Direction)> _deltas = [];
    private const int WheelDelta = 120;
    private const int IdleResetMilliseconds = 300;
    public void Reset() { _last.Clear(); _deltas.Clear(); _winMasked = false; _altMasked = false; }

    public void ObserveWin(bool held)
    {
        if (!held)
        {
            _winMasked = false;
            _altMasked = false;
            // A fresh Win hold must not inherit the previous hold's cooldown or partial swipe.
            // Its first swallowed input masks Start even if it has not reached a full notch.
            _last.Remove(WheelGesture.Desktop);
            _last.Remove(WheelGesture.MoveDesktop);
            _last.Remove(WheelGesture.Search);
            _deltas.Clear(); // only Win desktop gestures accumulate; a fresh hold never inherits a partial swipe
        }
    }

    public void ObserveAlt(bool held) { if (!held) _altMasked = false; }

    public static WheelGesture Route(bool win, bool alt, bool shift, bool ctrl) => (win, alt, shift, ctrl) switch
    {
        (true, false, false, false) => WheelGesture.Desktop,
        (true, false, true, false) => WheelGesture.MoveDesktop,
        (true, true, false, false) => WheelGesture.Search,
        (false, true, true, false) => WheelGesture.Resize,
        _ => WheelGesture.None,
    };

    public WheelDecision Accept(bool win, bool alt, bool shift, bool ctrl, long timestamp, int delta = WheelDelta)
    {
        ObserveWin(win);
        ObserveAlt(alt);
        var gesture = Route(win, alt, shift, ctrl);
        if (gesture == WheelGesture.None || delta == 0) return default;
        // Even a partial swipe is swallowed and must mask Start/Alt-menu on release.
        bool mask = win && (!_winMasked || (gesture == WheelGesture.Search && !_altMasked));
        if (mask) { _winMasked = true; if (alt) _altMasked = true; }
        bool accumulated = gesture is WheelGesture.Desktop or WheelGesture.MoveDesktop;
        long sum = 0;
        if (accumulated)
        {
            int direction = Math.Sign(delta);
            if (_deltas.TryGetValue(gesture, out var pending) && pending.Direction == direction &&
                timestamp >= pending.Timestamp && timestamp - pending.Timestamp < IdleResetMilliseconds)
                sum = pending.Sum;
            sum += delta;
            _deltas[gesture] = (sum, timestamp, direction);
            if (Math.Abs(sum) < WheelDelta) return new(gesture, false, mask);
        }
        int cooldown = gesture switch { WheelGesture.Desktop => 123, WheelGesture.MoveDesktop => 250,
            WheelGesture.Search => 40, _ => 25 };
        if (_last.TryGetValue(gesture, out long last) && timestamp - last < cooldown)
            return new(gesture, false, mask);
        _last[gesture] = timestamp;
        if (accumulated) _deltas[gesture] = (sum - Math.Sign(sum) * WheelDelta, timestamp, Math.Sign(delta));
        return new(gesture, true, mask);
    }
}
