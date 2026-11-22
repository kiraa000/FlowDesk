using System.Runtime.InteropServices;
using System.Text;
using FlowDesk.Core.Model;
using static FlowDesk.Core.Interop.Native;

namespace FlowDesk.Core.Hotkeys;

/// <summary>
/// Real window operations for the shortcut actions. All calls are non-blocking where the spec
/// requires it (ShowWindowAsync, SetWindowPos with NOACTIVATE) and use outer-rect physical pixels.
/// </summary>
public sealed class Win32WindowOps : IWindowOps
{
    private static readonly HashSet<string> ShellClasses = new(StringComparer.Ordinal)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd",
    };

    [DllImport("user32.dll")] private static extern nint MonitorFromPoint(POINT point, uint flags);
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(nint hwnd);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern nint BeginDeferWindowPos(int count);
    [DllImport("user32.dll")] private static extern nint DeferWindowPos(nint batch, nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool EndDeferWindowPos(nint batch);
    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW")] private static extern nint SendMinMax(nint hwnd, uint message, nint wParam, ref MinMax info, uint flags, uint timeout, out nuint result);
    [StructLayout(LayoutKind.Sequential)] private struct MinMax { public POINT Reserved, MaxSize, MaxPosition, MinTrack, MaxTrack; }

    public EdgeWindow? GetEdgeWindow(long handle)
    {
        nint h = (nint)handle;
        GetWindowThreadProcessId(h, out uint process);
        if (process == Environment.ProcessId) return null;
        if (!IsWindowVisible(h) || IsShellWindow(handle) || string.IsNullOrWhiteSpace(TitleOf(h)) ||
            (GetWindowLongPtr(h, GWL_EXSTYLE) & WS_EX_TOOLWINDOW) != 0 ||
            (DwmGetWindowAttribute(h, DWMWA_CLOAKED, out int cloak, sizeof(int)) == 0 && cloak != 0)) return null;
        var m = GetMetrics(handle);
        if (!m.Exists || m.IsMaximized || m.IsMinimized) return null;
        var frame = DwmGetWindowAttribute(h, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT f, Marshal.SizeOf<RECT>()) == 0
            ? PixelRect.FromLtrb(f.Left, f.Top, f.Right, f.Bottom) : m.Rect;
        if (frame.Width <= 0 || frame.Height <= 0) return null;
        var insets = new FrameInsets(frame.X - m.Rect.X, frame.Y - m.Rect.Y, m.Rect.Right - frame.Right, m.Rect.Bottom - frame.Bottom);
        uint dpi = GetDpiForWindow(h);
        var min = new MinMax();
        bool fixedWindow = SendMinMax(h, 0x24, 0, ref min, 2, 30, out _) == 0;
        return new(handle, frame, insets, dpi == 0 ? 1 : dpi / 96.0,
            Math.Max(0, min.MinTrack.X - insets.Left - insets.Right), Math.Max(0, min.MinTrack.Y - insets.Top - insets.Bottom), fixedWindow);
    }

    public PixelRect? GetVisibleFrame(long handle)
    {
        nint h = (nint)handle;
        if (!IsWindow(h) || IsIconic(h) || IsZoomed(h)) return null;
        if (DwmGetWindowAttribute(h, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT frame, Marshal.SizeOf<RECT>()) == 0)
            return PixelRect.FromLtrb(frame.Left, frame.Top, frame.Right, frame.Bottom);
        return GetWindowRect(h, out RECT outer) ? PixelRect.FromLtrb(outer.Left, outer.Top, outer.Right, outer.Bottom) : null;
    }

    public bool MoveBatch(IReadOnlyList<KeyValuePair<long, PixelRect>> moves)
    {
        if (moves.Count == 0) return true;
        if (moves.Any(p => GetEdgeWindow(p.Key) is null)) return false;
        nint batch = BeginDeferWindowPos(moves.Count);
        if (batch != 0)
        {
            foreach (var p in moves)
            {
                var r = p.Value;
                batch = DeferWindowPos(batch, (nint)p.Key, 0, r.X, r.Y, r.Width, r.Height, 0x214); // NOZORDER | NOACTIVATE | NOOWNERZORDER
                if (batch == 0) break;
            }
            if (batch != 0 && EndDeferWindowPos(batch)) return true;
        }
        // Deferred batches can fail (or partially apply cross-process). Read-back is authoritative;
        // ordered sequential fallback is idempotent and the bounded verifier repairs/refuses it.
        bool success = true;
        foreach (var p in moves) success &= GetEdgeWindow(p.Key) is not null && Move(p.Key, p.Value);
        return success;
    }

    public long GetForeground() => GetForegroundWindow();

    public bool IsShellWindow(long handle) =>
        handle != 0 && ShellClasses.Contains(ClassOf((nint)handle));
    public bool IsOwnWindow(long handle)
    {
        GetWindowThreadProcessId((nint)handle, out uint process);
        return process == Environment.ProcessId;
    }

    public WindowMetrics GetMetrics(long handle)
    {
        nint h = (nint)handle;
        if (h == 0 || !IsWindow(h)) return default;
        bool minimized = IsIconic(h), maximized = IsZoomed(h);
        var rect = GetWindowRect(h, out RECT r) ? PixelRect.FromLtrb(r.Left, r.Top, r.Right, r.Bottom) : default;
        return new WindowMetrics(rect, WorkArea(rect), minimized, maximized, true);
    }

    public void Restore(long handle) => ShowWindowAsync((nint)handle, SW_RESTORE);

    public void Maximize(long handle) => ShowWindowAsync((nint)handle, SW_MAXIMIZE);

    public void Minimize(long handle) => ShowWindowAsync((nint)handle, SW_MINIMIZE);

    public bool Move(long handle, PixelRect rect) =>
        SetWindowPos((nint)handle, 0, rect.X, rect.Y, rect.Width, rect.Height, SWP_NOZORDER | SWP_NOACTIVATE | 0x200);

    public bool Activate(long handle)
    {
        nint h = (nint)handle;
        if (h == 0 || !IsWindow(h)) return false;
        if (IsIconic(h)) ShowWindowAsync(h, SW_RESTORE);
        if (SetForegroundWindow(h)) return true;

        nint foreground = GetForegroundWindow();
        uint foregroundThread = GetWindowThreadProcessId(foreground, out _);
        uint ownThread = GetCurrentThreadId();
        bool attached = false;
        try
        {
            if (foregroundThread != 0 && foregroundThread != ownThread)
                attached = AttachThreadInput(ownThread, foregroundThread, true);
            BringWindowToTop(h);
            return SetForegroundWindow(h);
        }
        finally
        {
            if (attached) AttachThreadInput(ownThread, foregroundThread, false);
        }
    }

    public IReadOnlyList<FocusWindow> GetWindowsInZOrder()
    {
        var windows = new List<FocusWindow>();
        int z = 0;
        EnumWindows((h, _) =>
        {
            GetWindowThreadProcessId(h, out uint process);
            if (process == Environment.ProcessId) return true;
            var rect = GetWindowRect(h, out RECT r) ? PixelRect.FromLtrb(r.Left, r.Top, r.Right, r.Bottom) : default;
            long exStyle = GetWindowLongPtr(h, GWL_EXSTYLE);
            bool cloaked = DwmGetWindowAttribute(h, DWMWA_CLOAKED, out int value, sizeof(int)) == 0 && value != 0;
            windows.Add(new FocusWindow(
                h,
                rect,
                z++,
                IsWindowVisible(h),
                cloaked,
                IsIconic(h),
                TitleOf(h),
                ClassOf(h),
                (exStyle & WS_EX_TOOLWINDOW) != 0));
            return true;
        }, 0);
        return windows;
    }

    private static PixelRect WorkArea(PixelRect rect)
    {
        var point = new POINT { X = (int)(rect.X + rect.Width / 2.0), Y = (int)(rect.Y + rect.Height / 2.0) };
        nint monitor = MonitorFromPoint(point, MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
        if (monitor == 0 || !GetMonitorInfoW(monitor, ref info)) return rect;
        return PixelRect.FromLtrb(info.rcWork.Left, info.rcWork.Top, info.rcWork.Right, info.rcWork.Bottom);
    }

    private static string ClassOf(nint h)
    {
        var sb = new StringBuilder(256);
        return GetClassNameW(h, sb, sb.Capacity) > 0 ? sb.ToString() : "";
    }

    private static string TitleOf(nint h)
    {
        int length = GetWindowTextLengthW(h);
        if (length <= 0) return "";
        var sb = new StringBuilder(length + 1);
        GetWindowTextW(h, sb, sb.Capacity);
        return sb.ToString();
    }
}
