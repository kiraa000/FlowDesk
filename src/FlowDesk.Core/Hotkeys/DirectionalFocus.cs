using FlowDesk.Core.Model;

namespace FlowDesk.Core.Hotkeys;

/// <summary>The four directions used by snapping and directional focus.</summary>
public enum WindowDirection { Left, Right, Up, Down }

/// <summary>
/// One top-level window as seen by the shortcut layer. Flags are supplied by the OS layer so the
/// filter and ranking below stay pure and testable without touching real windows.
/// </summary>
public sealed record FocusWindow(
    long Handle,
    PixelRect Rect,
    int ZIndex,
    bool IsVisible,
    bool IsCloaked,
    bool IsMinimized,
    string Title,
    string ClassName,
    bool IsToolWindow);

/// <summary>
/// Focus-candidate filter and ranking for directional focus (Alt+Shift+arrows) and Alt+Q.
/// Candidates arrive in EnumWindows order (front to back); ties keep that order.
/// </summary>
public static class DirectionalFocus
{
    private static readonly HashSet<string> ShellClasses = new(StringComparer.Ordinal)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd",
    };

    /// <summary>
    /// Usable windows: not the active one, visible, not DWM-cloaked (on the current desktop),
    /// not minimized, non-empty title, not a shell window, no WS_EX_TOOLWINDOW.
    /// </summary>
    public static IReadOnlyList<FocusWindow> Usable(IReadOnlyList<FocusWindow> windows, long activeHandle)
    {
        var result = new List<FocusWindow>(windows.Count);
        foreach (var window in windows)
        {
            if (window.Handle == activeHandle) continue;
            if (!window.IsVisible || window.IsCloaked || window.IsMinimized) continue;
            if (string.IsNullOrEmpty(window.Title)) continue;
            if (window.IsToolWindow || ShellClasses.Contains(window.ClassName)) continue;
            result.Add(window);
        }
        return result;
    }

    /// <summary>
    /// Directional candidates sorted by score = primary + 1.5 * secondary. Left/right use |dx| as
    /// primary and |dy| as secondary; up/down use |dy| as primary and |dx| as secondary.
    /// OrderBy is stable, so equal scores keep their incoming z-order.
    /// </summary>
    public static IReadOnlyList<FocusWindow> Rank(
        PixelRect activeRect, IEnumerable<FocusWindow> usable, WindowDirection direction)
    {
        double ax = CenterX(activeRect), ay = CenterY(activeRect);
        var scored = new List<(FocusWindow Window, double Score)>();
        foreach (var window in usable)
        {
            double dx = CenterX(window.Rect) - ax;
            double dy = CenterY(window.Rect) - ay;
            double? primary = direction switch
            {
                WindowDirection.Left => dx < 0 ? -dx : null,
                WindowDirection.Right => dx > 0 ? dx : null,
                WindowDirection.Up => dy < 0 ? -dy : null,
                WindowDirection.Down => dy > 0 ? dy : null,
                _ => null,
            };
            if (primary is null) continue;
            double secondary = direction is WindowDirection.Left or WindowDirection.Right ? Math.Abs(dy) : Math.Abs(dx);
            scored.Add((window, primary.Value + 1.5 * secondary));
        }
        return scored.OrderBy(item => item.Score).Select(item => item.Window).ToList();
    }

    /// <summary>The min(steps, count)-th ranked window (1-based), or null when none qualifies.</summary>
    public static long? Pick(
        PixelRect activeRect, IReadOnlyList<FocusWindow> usable, WindowDirection direction, int steps)
    {
        if (steps <= 0) return null;
        var ranked = Rank(activeRect, usable, direction);
        if (ranked.Count == 0) return null;
        return ranked[Math.Min(steps, ranked.Count) - 1].Handle;
    }

    private static double CenterX(PixelRect rect) => rect.X + rect.Width / 2.0;
    private static double CenterY(PixelRect rect) => rect.Y + rect.Height / 2.0;
}
