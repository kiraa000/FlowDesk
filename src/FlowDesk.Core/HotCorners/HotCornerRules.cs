using FlowDesk.Core.Hotkeys;

namespace FlowDesk.Core.HotCorners;

public enum HotCorner { TopLeft, TopRight, BottomLeft, BottomRight }
public enum HotCornerAction { None, TaskView, ShowDesktop, StartFocus, Lock, CommandPalette }
public static class HotCornerActionKeys
{
    public static ShortcutKeyStroke[] For(HotCornerAction action)
    {
        ushort key = action switch { HotCornerAction.TaskView => 0x09, HotCornerAction.ShowDesktop => 0x44, _ => (ushort)0 };
        return key == 0 ? [] : [new(0x5B, false, true), new(key, false), new(key, true), new(0x5B, true, true)];
    }
    public static ShortcutKeyStroke[] ReleaseAfterFailure(HotCornerAction action)
    {
        var chord = For(action);
        return chord.Length == 0 ? [] : [new(0xE8, false), new(0xE8, true), chord[2], chord[3]];
    }
}
public readonly record struct InputHookRequirements(bool Mouse, bool Keyboard)
{
    public static InputHookRequirements For(bool windowShortcuts, bool quickLook, bool hotCorners) =>
        new(windowShortcuts || hotCorners, windowShortcuts || quickLook);
}
public sealed record HotCornerActionOption(HotCornerAction Action, string Label);
public sealed record HotCornerConfiguration(bool Enabled = false, HotCornerAction TopLeft = HotCornerAction.TaskView,
    HotCornerAction TopRight = HotCornerAction.None, HotCornerAction BottomLeft = HotCornerAction.None,
    HotCornerAction BottomRight = HotCornerAction.ShowDesktop)
{
    public static IReadOnlyList<HotCornerActionOption> Actions { get; } = Array.AsReadOnly(new[] {
        new HotCornerActionOption(HotCornerAction.None, "None"), new(HotCornerAction.TaskView, "Task View"),
        new(HotCornerAction.ShowDesktop, "Show desktop"), new(HotCornerAction.StartFocus, "Start Focus"), new(HotCornerAction.Lock, "Lock"), new(HotCornerAction.CommandPalette, "Open command palette") });
    public HotCornerAction For(HotCorner corner)
    {
        var value = corner switch { HotCorner.TopLeft => TopLeft, HotCorner.TopRight => TopRight,
            HotCorner.BottomLeft => BottomLeft, HotCorner.BottomRight => BottomRight, _ => HotCornerAction.None };
        return Enum.IsDefined(value) ? value : HotCornerAction.None;
    }
}
public readonly record struct CornerPoint(int X, int Y);
public readonly record struct CornerBounds(int Left, int Top, int Right, int Bottom)
{
    public bool Valid => Right > Left && Bottom > Top;
    public bool Contains(long x, long y) => Valid && x >= Left && x < Right && y >= Top && y < Bottom;
}
public readonly record struct CornerLocation(HotCorner Corner, CornerPoint Point);
public static class HotCornerGeometry
{
    public static IEnumerable<CornerLocation> Corners(CornerBounds monitor)
    {
        if (!monitor.Valid) yield break;
        yield return new(HotCorner.TopLeft, new(monitor.Left, monitor.Top));
        yield return new(HotCorner.TopRight, new(monitor.Right - 1, monitor.Top));
        yield return new(HotCorner.BottomLeft, new(monitor.Left, monitor.Bottom - 1));
        yield return new(HotCorner.BottomRight, new(monitor.Right - 1, monitor.Bottom - 1));
    }
    public static bool Outer(CornerLocation corner, IReadOnlyList<CornerBounds> monitors)
    {
        int dx = corner.Corner is HotCorner.TopLeft or HotCorner.BottomLeft ? -1 : 1;
        int dy = corner.Corner is HotCorner.TopLeft or HotCorner.TopRight ? -1 : 1;
        return !monitors.Any(m => m.Contains((long)corner.Point.X + dx, corner.Point.Y) || m.Contains(corner.Point.X, (long)corner.Point.Y + dy));
    }
    public static CornerLocation? At(CornerPoint point, IReadOnlyList<CornerBounds> monitors) =>
        monitors.SelectMany(Corners).Where(c => c.Point == point).Select(c => (CornerLocation?)c).FirstOrDefault();
    /// <summary>
    /// The low-level mouse hook reports the unclipped position: a real mouse pushed into a corner gives e.g.
    /// (2615,-56) while the cursor stops at (2559,0). Clamp the hook point into the cursor's monitor before
    /// comparing, otherwise every physical corner push fails the consistency check ("checks unavailable").
    /// </summary>
    public static bool HookMatchesCursor(CornerPoint hook, CornerPoint cursor, IReadOnlyList<CornerBounds> monitors)
    {
        if (hook == cursor) return true;
        if (monitors.Any(m => m.Contains(hook.X, hook.Y))) return false; // on-screen hook point must match exactly
        foreach (var m in monitors)
        {
            if (!m.Contains(cursor.X, cursor.Y)) continue;
            var clamped = new CornerPoint(Math.Clamp(hook.X, m.Left, m.Right - 1), Math.Clamp(hook.Y, m.Top, m.Bottom - 1));
            return clamped == cursor;
        }
        return false;
    }
    public static bool Near(CornerPoint a, CornerPoint b, int radius) =>
        Math.Abs((long)a.X - b.X) <= radius && Math.Abs((long)a.Y - b.Y) <= radius;
    public static bool CoversMonitor(CornerBounds foreground, IReadOnlyList<CornerBounds> monitors) => foreground.Valid &&
        monitors.Any(m => m.Valid && foreground.Left <= m.Left && foreground.Top <= m.Top && foreground.Right >= m.Right && foreground.Bottom >= m.Bottom);
    /// <summary>A maximized or captioned window that covers a monitor (monitor without a taskbar, or an
    /// auto-hide taskbar) is a normal window, not full-screen. Real full-screen apps/games/video/F11 use an
    /// un-maximized caption-less window; busy/presentation/D3D states are caught by the notification state.</summary>
    public static bool IsFullScreen(bool coversMonitor, bool maximized, bool hasCaption) => coversMonitor && !maximized && !hasCaption;
}
public static class HotCornerGate
{
    public static string? BlockReason(bool checksSucceeded, bool defaultInputDesktop, bool sessionBlocked,
        int notificationState, bool coversMonitor, bool shellForeground, bool buttons, bool modifiers)
    {
        if (!checksSucceeded) return "checks unavailable";
        if (!defaultInputDesktop || sessionBlocked) return "locked or secure desktop";
        if (buttons) return "button held";
        if (modifiers) return "modifier held";
        if (notificationState is 1 or 2 or 3 or 4 || notificationState is < 1 or > 7) return "busy/presentation/D3D";
        if (coversMonitor && !shellForeground) return "full-screen window";
        return null;
    }
}
