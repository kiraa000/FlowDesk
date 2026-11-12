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

    public long GetForeground() => GetForegroundWindow();

    public bool IsShellWindow(long handle) =>
        handle != 0 && ShellClasses.Contains(ClassOf((nint)handle));

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
        SetWindowPos((nint)handle, 0, rect.X, rect.Y, rect.Width, rect.Height, SWP_NOZORDER | SWP_NOACTIVATE);

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
