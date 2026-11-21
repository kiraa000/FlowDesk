using System.Runtime.InteropServices;
using System.Text;
using FlowDesk.Core.Model;
using FlowDesk.Core.Desktops;
using FlowDesk.Core.Capture;
using static FlowDesk.Core.Interop.Native;

namespace FlowDesk.Core.Windows;

/// <summary>
/// Real window access through Win32. The calling process must be Per-Monitor-V2 DPI aware
/// (WinUI 3 apps are by default; the probe tool calls <see cref="EnsurePerMonitorDpiAwareness"/>),
/// otherwise every rectangle here is virtualised and restores land in the wrong place.
/// </summary>
public sealed class Win32WindowSystem(IVirtualDesktops? desktops = null) : IWindowSystem
{
    private readonly IVirtualDesktops _desktops = desktops ?? VirtualDesktopService.ReadOnly;
    public MonitorNames DisplayNames { get; } = new();
    private static readonly HashSet<string> ShellClasses = new(StringComparer.Ordinal)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd",
        "Windows.UI.Core.CoreWindow", // bare CoreWindows are hosted by a frame or are shell UI (Start, Search)
        "XamlExplorerHostIslandWindow", "TopLevelWindowForOverflowXamlIsland",
    };

    private readonly ProcessResolver _processes = new();
    // Frame handle -> app process last seen inside it (frames keep their handle while the app is suspended).
    private readonly System.Collections.Concurrent.ConcurrentDictionary<nint, int> _frameApps = new();

    private int DetachedCoreWindowPid(nint frame, int framePid, WindowDesktopInfo? frameDesktop)
    {
        string title = TitleOf(frame);
        if (title.Length == 0) return 0;
        _frameApps.TryGetValue(frame, out int known);
        var candidates = new List<int>();
        for (nint c = FindWindowExW(0, 0, "Windows.UI.Core.CoreWindow", title); c != 0;
             c = FindWindowExW(0, c, "Windows.UI.Core.CoreWindow", title))
        {
            GetWindowThreadProcessId(c, out uint cp);
            if (cp == 0 || cp == framePid) continue;
            if (known != 0 && cp == known) return known; // same app as before: unambiguous
            // A fresh all-desktop capture can see several same-title suspended apps. Do not infer
            // their content PID from a positively different desktop; preserve the existing cache
            // preference (also while shell metadata is settling after a move).
            if (frameDesktop is { HasIdentity: true } desktop && desktop.ConflictsWith(WindowDesktopInfo.Read(_desktops, c))) continue;
            candidates.Add((int)cp);
        }
        return SelectDetachedProcess(known, candidates);
    }

    // Multiple same-title suspended apps are common once every desktop is enumerated. Unknown
    // desktop metadata must not let the first z-order candidate masquerade as another frame's app.
    internal static int SelectDetachedProcess(int known, IEnumerable<int> candidates)
    {
        var distinct = candidates.Where(pid => pid > 0).ToHashSet();
        return known > 0 && distinct.Contains(known) ? known : distinct.Count == 1 ? distinct.First() : 0;
    }

    public int OwnProcessId { get; } = Environment.ProcessId;

    public static void EnsurePerMonitorDpiAwareness() => SetProcessDpiAwarenessContext(-4); // PER_MONITOR_AWARE_V2

    public IReadOnlyList<WindowInfo> GetAppWindows() => EnumerateAppWindows(includeOtherDesktops: false);
    public IReadOnlyList<WindowInfo> GetAllAppWindows() => EnumerateAppWindows(includeOtherDesktops: true);

    private IReadOnlyList<WindowInfo> EnumerateAppWindows(bool includeOtherDesktops)
    {
        var handles = new List<nint>();
        EnumWindows((h, _) => { handles.Add(h); return true; }, 0); // z-order, front to back
        nint shell = GetShellWindow();
        var result = new List<WindowInfo>(handles.Count / 4);
        foreach (var h in handles)
        {
            if (h == shell || !IsAppWindow(h, includeOtherDesktops, out var desktop, out int cloak)) continue;
            var info = Describe(h, desktop, cloak);
            if (info is not null) result.Add(info);
        }
        return result;
    }

    public WindowInfo? Get(long handle) => IsWindow((nint)handle) ? Describe((nint)handle) : null;

    public bool IsAlive(long handle) => IsWindow((nint)handle);

    /// <summary>Alt-Tab rules, with only positively identified other-desktop shell cloaks admitted.</summary>
    private bool IsAppWindow(nint h, bool includeOtherDesktops, out WindowDesktopInfo desktop, out int cloak)
    {
        desktop = default;
        cloak = 0;
        if (!IsWindowVisible(h)) return false;
        long ex = GetWindowLongPtr(h, GWL_EXSTYLE);
        if ((ex & WS_EX_TOOLWINDOW) != 0) return false;
        bool appWindow = (ex & WS_EX_APPWINDOW) != 0;
        if (!appWindow)
        {
            if ((ex & WS_EX_TOOLWINDOW) != 0 || (ex & WS_EX_NOACTIVATE) != 0) return false;
            nint owner = GetWindow(h, GW_OWNER);
            if (owner != 0 && IsWindowVisible(owner)) return false;
        }
        if (GetWindowTextLengthW(h) == 0) return false;
        if (ShellClasses.Contains(ClassOf(h))) return false;
        if (DwmGetWindowAttribute(h, DWMWA_CLOAKED, out int reason, sizeof(int)) != 0) return false;
        cloak = reason;
        if (cloak != 0 && (!includeOtherDesktops || cloak != 2)) return false;
        // Current-desktop clients (notably the 750ms focus loop) keep the old cheap DWM-only path.
        // Workspace/all-desktop clients explicitly pay for desktop identity lookup.
        if (includeOtherDesktops) desktop = WindowDesktopInfo.Read(_desktops, h);
        return DesktopCaptureFilter.AllowCloaked(cloak, desktop, includeOtherDesktops);
    }

    private WindowInfo? Describe(nint h, WindowDesktopInfo? knownDesktop = null, int? knownCloak = null)
    {
        GetWindowThreadProcessId(h, out uint rawPid);
        if (rawPid == 0) return null;
        int pid = (int)rawPid;
        string cls = ClassOf(h);
        bool unresolved = false;
        var desktop = knownDesktop ?? WindowDesktopInfo.Read(_desktops, h);

        if (cls == "ApplicationFrameWindow")
        {
            // UWP: the frame belongs to ApplicationFrameHost.exe; the real app owns a child CoreWindow.
            int framePid = pid, appPid = 0;
            EnumChildWindows(h, (c, _) =>
            {
                GetWindowThreadProcessId(c, out uint cp);
                if (cp != 0 && cp != framePid) { appPid = (int)cp; return false; }
                return true;
            }, 0);
            // A minimized (suspended) UWP app detaches its CoreWindow from the frame and makes it a top-level
            // window with the same title. Without this the frame looks like ApplicationFrameHost itself, can't be
            // matched, and switching back starts the app again while its old window stays minimized.
            if (appPid == 0) appPid = DetachedCoreWindowPid(h, framePid, desktop);
            if (appPid != 0) { pid = appPid; _frameApps[h] = appPid; } else unresolved = true;
        }

        var proc = _processes.Resolve(pid);
        var (rect, show) = NormalRectAndShow(h);
        int cloak = knownCloak ?? (DwmGetWindowAttribute(h, DWMWA_CLOAKED, out int reason, sizeof(int)) == 0 ? reason : -1);
        return new WindowInfo
        {
            Handle = h,
            ProcessId = pid,
            ExePath = proc.ExePath,
            Aumid = proc.Aumid,
            Arguments = proc.Arguments,
            Title = TitleOf(h),
            ClassName = cls,
            NormalRect = rect,
            Show = show,
            MonitorDevice = MonitorOf(h)?.szDevice ?? "",
            Unresolved = unresolved,
            DesktopIndex = desktop.Index, DesktopId = desktop.Id, IsOnCurrentDesktop = desktop.IsCurrent, CloakReason = cloak,
        };
    }

    /// <summary>
    /// Outer rect of the window in its NORMAL state. For minimized/maximized windows that is the
    /// placement's restore rect, which is in workspace coordinates and must be shifted to screen ones.
    /// </summary>
    private static (PixelRect, WindowShow) NormalRectAndShow(nint h)
    {
        bool min = IsIconic(h), max = IsZoomed(h);
        var show = min ? WindowShow.Minimized : max ? WindowShow.Maximized : WindowShow.Normal;
        if (!min && !max && GetWindowRect(h, out var r))
            return (PixelRect.FromLtrb(r.Left, r.Top, r.Right, r.Bottom), show);

        var wp = new WINDOWPLACEMENT { length = Marshal.SizeOf<WINDOWPLACEMENT>() };
        if (!GetWindowPlacement(h, ref wp)) return (default, show);
        var n = wp.rcNormalPosition;
        var mi = MonitorOf(h);
        int dx = 0, dy = 0;
        if (mi is { } m && (GetWindowLongPtr(h, GWL_EXSTYLE) & WS_EX_TOOLWINDOW) == 0)
        {
            dx = m.rcWork.Left - m.rcMonitor.Left;
            dy = m.rcWork.Top - m.rcMonitor.Top;
        }
        return (PixelRect.FromLtrb(n.Left + dx, n.Top + dy, n.Right + dx, n.Bottom + dy), show);
    }

    public IReadOnlyList<MonitorInfo> GetMonitors()
    {
        var metadata = DisplayConfigNames.Read();
        var list = new List<MonitorInfo>();
        EnumDisplayMonitors(0, 0, (nint hm, nint _, ref RECT _, nint _) =>
        {
            var mi = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
            if (GetMonitorInfoW(hm, ref mi))
            {
                int dpi = GetDpiForMonitor(hm, 0, out uint dx, out _) == 0 ? (int)dx : 96;
                metadata.TryGetValue(mi.szDevice, out var details);
                list.Add(new MonitorInfo(mi.szDevice,
                    PixelRect.FromLtrb(mi.rcMonitor.Left, mi.rcMonitor.Top, mi.rcMonitor.Right, mi.rcMonitor.Bottom),
                    PixelRect.FromLtrb(mi.rcWork.Left, mi.rcWork.Top, mi.rcWork.Right, mi.rcWork.Bottom),
                    (mi.dwFlags & MONITORINFOF_PRIMARY) != 0, dpi,
                    details?.FriendlyName ?? "", details?.StableId ?? "", details?.IsInternal ?? false));
            }
            return true;
        }, 0);
        DisplayNames.Observe(list);
        return list;
    }

    public PlaceOutcome Place(long handle, PixelRect target, WindowShow show)
    {
        var desktop = WindowDesktopInfo.Read(_desktops, handle);
        int cloak = DwmGetWindowAttribute((nint)handle, DWMWA_CLOAKED, out int reason, sizeof(int)) == 0 ? reason : -1;
        bool canActivate = desktop.IsCurrent == true || (desktop.IsCurrent is null && cloak == 0);
        return WindowPlacer.Place(new NativePlacementApi((nint)handle, canActivate), target, show);
    }

    private sealed class NativePlacementApi(nint h, bool canActivate) : IWindowPlacementApi
    {
        public bool IsWindow => NativeIsWindow(h);
        private static bool NativeIsWindow(nint handle) => Interop.Native.IsWindow(handle);
        public bool IsHung => IsHungAppWindow(h);
        public bool IsMinimized => IsIconic(h);
        public bool IsMaximized => IsZoomed(h);
        public bool CanActivate => canActivate;
        public uint Dpi => GetDpiForWindow(h);
        public int LastError => Marshal.GetLastWin32Error();
        public bool IsOnTargetMonitor(PixelRect target)
        {
            var r = new RECT { Left = target.X, Top = target.Y, Right = target.Right, Bottom = target.Bottom };
            return MonitorFromWindow(h, MONITOR_DEFAULTTONEAREST) == MonitorFromRect(ref r, MONITOR_DEFAULTTONEAREST);
        }
        public bool SetRect(PixelRect target, bool asynchronous = false) =>
            SetWindowPos(h, 0, target.X, target.Y, target.Width, target.Height,
                SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOOWNERZORDER | (asynchronous ? SWP_ASYNCWINDOWPOS : 0));
        public bool SetPlacementAsync(PixelRect target, WindowShow show)
        {
            if (!canActivate && show == WindowShow.Maximized && (!IsZoomed(h) || !IsOnTargetMonitor(target)))
            { Marshal.SetLastPInvokeError(0); return false; }
            var wp = new WINDOWPLACEMENT { length = Marshal.SizeOf<WINDOWPLACEMENT>() };
            if (!GetWindowPlacement(h, ref wp)) return false;
            var r = new RECT { Left = target.X, Top = target.Y, Right = target.Right, Bottom = target.Bottom };
            nint monitor = MonitorFromRect(ref r, MONITOR_DEFAULTTONEAREST);
            var mi = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
            if (!GetMonitorInfoW(monitor, ref mi)) return false;
            var normal = WindowPlacer.ToWorkspaceRect(target,
                PixelRect.FromLtrb(mi.rcMonitor.Left, mi.rcMonitor.Top, mi.rcMonitor.Right, mi.rcMonitor.Bottom),
                PixelRect.FromLtrb(mi.rcWork.Left, mi.rcWork.Top, mi.rcWork.Right, mi.rcWork.Bottom),
                (GetWindowLongPtr(h, GWL_EXSTYLE) & WS_EX_TOOLWINDOW) != 0);
            wp.flags = 0x0004; // WPF_ASYNCWINDOWPLACEMENT; clear stale RESTORETOMAXIMIZED/min-position flags
            wp.showCmd = WindowPlacer.AsyncShowCommand(show, IsIconic(h), IsZoomed(h), IsOnTargetMonitor(target), canActivate);
            wp.rcNormalPosition = new RECT { Left = normal.X, Top = normal.Y, Right = normal.Right, Bottom = normal.Bottom };
            return SetWindowPlacement(h, ref wp);
        }
        public bool RectMatches(PixelRect target) => Win32WindowSystem.RectMatches(h, target);
        public void Show(int command) => ShowWindow(h, command);
        public void Pause() => Thread.Sleep(30);
    }

    private static bool RectMatches(nint h, PixelRect t) =>
        GetWindowRect(h, out var r) && r.Left == t.X && r.Top == t.Y && r.Right == t.Right && r.Bottom == t.Bottom;

    public (int L, int T, int R, int B) GetInvisibleBorders(long handle)
    {
        nint h = (nint)handle;
        if (IsIconic(h) || IsZoomed(h)) return (0, 0, 0, 0);
        if (!GetWindowRect(h, out var r)) return (0, 0, 0, 0);
        if (DwmGetWindowAttribute(h, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT f, Marshal.SizeOf<RECT>()) != 0) return (0, 0, 0, 0);
        return (Math.Max(0, f.Left - r.Left), Math.Max(0, f.Top - r.Top), Math.Max(0, r.Right - f.Right), Math.Max(0, r.Bottom - f.Bottom));
    }

    public void BringToFront(long handle) =>
        SetWindowPos((nint)handle, HWND_TOP, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_ASYNCWINDOWPOS);

    public bool Activate(long handle) => SetForegroundWindow((nint)handle);

    public void Close(long handle) => PostMessageW((nint)handle, WM_CLOSE, 0, 0);

    public void Minimize(long handle) => ShowWindowAsync((nint)handle, SW_SHOWMINNOACTIVE);

    private static MONITORINFOEX? MonitorOf(nint h)
    {
        nint hm = MonitorFromWindow(h, MONITOR_DEFAULTTONEAREST);
        var mi = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
        return hm != 0 && GetMonitorInfoW(hm, ref mi) ? mi : null;
    }

    private static string ClassOf(nint h)
    {
        var sb = new StringBuilder(256);
        return GetClassNameW(h, sb, sb.Capacity) > 0 ? sb.ToString() : "";
    }

    private static string TitleOf(nint h)
    {
        int len = GetWindowTextLengthW(h);
        if (len <= 0) return "";
        var sb = new StringBuilder(len + 1);
        GetWindowTextW(h, sb, sb.Capacity);
        return sb.ToString();
    }
}
