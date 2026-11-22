using FlowDesk.Core.Model;

namespace FlowDesk.Core.Hotkeys;

/// <summary>Outer rect, work area and show state of one window. Exists is false when the handle is gone.</summary>
public readonly record struct WindowMetrics(
    PixelRect Rect,
    PixelRect WorkArea,
    bool IsMinimized,
    bool IsMaximized,
    bool Exists);

/// <summary>
/// The window primitives the shortcut actions need. The real implementation is
/// <see cref="Win32WindowOps"/>; tests use a fake so no window is ever touched.
/// </summary>
public interface IWindowOps
{
    /// <summary>GetForegroundWindow handle, 0 when there is none.</summary>
    long GetForeground();

    /// <summary>True for the shell classes the shortcuts must never act on.</summary>
    bool IsShellWindow(long handle);
    bool IsOwnWindow(long handle) => false;

    WindowMetrics GetMetrics(long handle);

    /// <summary>Visible frame and invisible border insets. Null means the window cannot be resized safely.</summary>
    EdgeWindow? GetEdgeWindow(long handle)
    {
        var m = GetMetrics(handle);
        return m.Exists && !m.IsMaximized && !m.IsMinimized ? new(handle, m.Rect) : null;
    }
    /// <summary>Fast frame-only read for the WinEvent callback; must not send messages to another process.</summary>
    PixelRect? GetVisibleFrame(long handle) => GetEdgeWindow(handle)?.Frame;

    /// <summary>Ordered shrink-before-grow batch; implementations must not activate or reorder windows.</summary>
    bool MoveBatch(IReadOnlyList<KeyValuePair<long, PixelRect>> moves)
    {
        bool success = true;
        foreach (var move in moves) success &= Move(move.Key, move.Value);
        return success;
    }

    /// <summary>ShowWindowAsync(SW_RESTORE).</summary>
    void Restore(long handle);

    /// <summary>ShowWindowAsync(SW_MAXIMIZE).</summary>
    void Maximize(long handle);

    /// <summary>ShowWindowAsync(SW_MINIMIZE).</summary>
    void Minimize(long handle);

    /// <summary>SetWindowPos with the outer rect, NOZORDER | NOACTIVATE.</summary>
    bool Move(long handle, PixelRect rect);

    /// <summary>Restore if minimized, then SetForegroundWindow with one AttachThreadInput retry.</summary>
    bool Activate(long handle);

    /// <summary>All top-level windows in EnumWindows z-order, with the flags the focus filter needs.</summary>
    IReadOnlyList<FocusWindow> GetWindowsInZOrder();
}
