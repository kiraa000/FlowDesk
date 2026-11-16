namespace FlowDesk.Core.Hotkeys;

public enum WheelGesture { None, Resize, Desktop, MoveDesktop, Search }

public readonly record struct WheelDecision(WheelGesture Gesture, bool Accepted, bool MaskMenu);

/// <summary>Message-thread-only input policy. Suppression includes throttled events, not unrelated modifiers.</summary>
public sealed class WheelGestures
{
    private bool _winMasked;
    private bool _altMasked;
    private readonly Dictionary<WheelGesture, long> _last = [];
    public void Reset() { _last.Clear(); _winMasked = false; _altMasked = false; }

    public void ObserveWin(bool held)
    {
        if (!held)
        {
            _winMasked = false;
            _altMasked = false;
            // Each fresh Win hold must accept its first gesture (and therefore mask Start),
            // even when the previous hold ended inside the cooldown interval.
            _last.Remove(WheelGesture.Desktop);
            _last.Remove(WheelGesture.MoveDesktop);
            _last.Remove(WheelGesture.Search);
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

    public WheelDecision Accept(bool win, bool alt, bool shift, bool ctrl, long timestamp)
    {
        ObserveWin(win);
        ObserveAlt(alt);
        var gesture = Route(win, alt, shift, ctrl);
        if (gesture == WheelGesture.None) return default;
        int cooldown = gesture switch { WheelGesture.Desktop => 123, WheelGesture.MoveDesktop => 250,
            WheelGesture.Search => 40, _ => 25 };
        if (_last.TryGetValue(gesture, out long last) && timestamp - last < cooldown)
            return new(gesture, false, false);
        _last[gesture] = timestamp;
        bool mask = win && (!_winMasked || (gesture == WheelGesture.Search && !_altMasked));
        if (mask) { _winMasked = true; if (alt) _altMasked = true; }
        return new(gesture, true, mask);
    }
}
