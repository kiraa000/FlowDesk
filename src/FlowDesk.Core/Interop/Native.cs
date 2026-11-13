// Win32 declarations used by the window core. Kept in one place so the rest of
// the code reads as plain C#. Only what FlowDesk actually calls is declared.
using System.Runtime.InteropServices;
using System.Text;

namespace FlowDesk.Core.Interop;

internal static class Native
{
    public delegate bool EnumWindowsProc(nint hwnd, nint lParam);
    public delegate bool MonitorEnumProc(nint hMonitor, nint hdc, ref RECT rect, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct WINDOWPLACEMENT
    {
        public int length;
        public int flags;
        public int showCmd;
        public POINT ptMinPosition;
        public POINT ptMaxPosition;
        public RECT rcNormalPosition;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public int dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    public const int GWL_STYLE = -16, GWL_EXSTYLE = -20;
    public const long WS_VISIBLE = 0x10000000, WS_CHILD = 0x40000000, WS_MINIMIZE = 0x20000000, WS_MAXIMIZE = 0x01000000;
    public const long WS_EX_TOOLWINDOW = 0x00000080, WS_EX_APPWINDOW = 0x00040000, WS_EX_NOACTIVATE = 0x08000000;
    public const uint GW_OWNER = 4;

    public const int SW_HIDE = 0, SW_SHOWNORMAL = 1, SW_SHOWMINIMIZED = 2, SW_SHOWMAXIMIZED = 3,
        SW_SHOWNOACTIVATE = 4, SW_MINIMIZE = 6, SW_SHOWMINNOACTIVE = 7, SW_RESTORE = 9;

    public const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010,
        SWP_NOOWNERZORDER = 0x0200, SWP_ASYNCWINDOWPOS = 0x4000;
    public static readonly nint HWND_TOP = 0;

    public const uint WM_CLOSE = 0x0010;
    public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9, DWMWA_CLOAKED = 14;
    public const uint MONITOR_DEFAULTTONEAREST = 2;
    public const int MONITORINFOF_PRIMARY = 1;
    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    public const int ProcessCommandLineInformation = 60;
    public const int APPMODEL_ERROR_NO_APPLICATION = 15703;
    public const int ERROR_INSUFFICIENT_BUFFER = 122;

    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, nint lParam);
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(nint parent, EnumWindowsProc cb, nint lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern nint FindWindowExW(nint parent, nint childAfter, string? className, string? windowName);
    [DllImport("user32.dll")] public static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] public static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] public static extern bool IsZoomed(nint hwnd);
    [DllImport("user32.dll")] public static extern bool IsHungAppWindow(nint hwnd);
    [DllImport("user32.dll")] public static extern nint GetWindow(nint hwnd, uint cmd);
    [DllImport("user32.dll")] public static extern nint GetShellWindow();
    [DllImport("user32.dll")] public static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowTextLengthW(nint hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowTextW(nint hwnd, StringBuilder sb, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassNameW(nint hwnd, StringBuilder sb, int max);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(nint hwnd, out RECT rect);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool GetWindowPlacement(nint hwnd, ref WINDOWPLACEMENT wp);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool SetWindowPlacement(nint hwnd, ref WINDOWPLACEMENT wp);
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool ShowWindowAsync(nint hwnd, int cmd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(nint hwnd, int cmd);
    public const int SW_MAXIMIZE = 3, SW_SHOWNA = 8;
    [DllImport("user32.dll")] public static extern bool PostMessageW(nint hwnd, uint msg, nint w, nint l);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll")] public static extern nint MonitorFromWindow(nint hwnd, uint flags);
    [DllImport("user32.dll")] public static extern nint MonitorFromRect(ref RECT rect, uint flags);
    [DllImport("user32.dll")] public static extern bool EnumDisplayMonitors(nint hdc, nint clip, MonitorEnumProc cb, nint lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool GetMonitorInfoW(nint hMonitor, ref MONITORINFOEX info);
    [DllImport("user32.dll")] public static extern nint SetProcessDpiAwarenessContext(nint value);

    [DllImport("shcore.dll")] public static extern int GetDpiForMonitor(nint hMonitor, int type, out uint dpiX, out uint dpiY);

    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(nint hwnd, int attr, out RECT value, int size);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(nint hwnd, int attr, out int value, int size);

    [DllImport("kernel32.dll", SetLastError = true)] public static extern nint OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] public static extern bool CloseHandle(nint h);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool QueryFullProcessImageNameW(nint hProcess, int flags, StringBuilder name, ref int size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetApplicationUserModelId(nint hProcess, ref int length, StringBuilder? aumid);

    [DllImport("ntdll.dll")]
    public static extern int NtQueryInformationProcess(nint hProcess, int infoClass, nint buffer, int length, out int returnLength);

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    public static extern int AssocQueryStringW(int flags, int str, string assoc, string? extra, StringBuilder? output, ref int outLen);
    public const int ASSOCSTR_EXECUTABLE = 2;
}
