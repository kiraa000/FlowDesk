using System.Diagnostics;
using System.Runtime.InteropServices;
using FlowDesk.Core.Capture;
using FlowDesk.Core.Hotkeys;
using FlowDesk.Core.Palette;
using FlowDesk.Core.Windows;

namespace FlowDesk.App.Services;

internal sealed class PaletteWindowSwitcher(AppServices services, Func<string, Task> copy, Action<string> notify, Action hide)
{
    private const int MaxCachedNames = 256;

    private readonly Win32WindowOps ops = new();
    private readonly Dictionary<string, string> cachedNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly object namesGate = new();

    internal CommandItem[] Snapshot(IReadOnlyList<CommandItem> apps)
    {
        long started = Stopwatch.GetTimestamp();
        var windows = services.Windows.GetAllAppWindows();
        var appNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var app in apps) appNames.TryAdd(app.Id, app.Title);
        int desktopCount = services.Desktops.GetCount() ?? 0;
        var items = new List<CommandItem>();
        foreach (var info in windows)
        {
            nint hwnd = (nint)info.Handle;
            GetWindowThreadProcessId(hwnd, out uint owner);
            if (owner == 0 || owner == services.Windows.OwnProcessId || string.IsNullOrWhiteSpace(info.Title)) continue;
            string icon = !string.IsNullOrEmpty(info.Aumid) ? "shell:AppsFolder\\" + info.Aumid : info.ExePath ?? "\uE737";
            string name = appNames.GetValueOrDefault("app:" + (info.Aumid ?? info.ExePath)) ?? FriendlyName(info);
            if (string.IsNullOrWhiteSpace(name)) name = "Window";
            string subtitle = desktopCount > 1 && info.DesktopIndex is int index ? $"{name} · Desktop {index + 1}" : name;
            Task Switch() { SwitchTo(info, owner); return Task.CompletedTask; }
            items.Add(new($"window:{hwnd:X}:{owner}", info.Title, subtitle, CommandKind.Window, icon, Switch, Synonyms: name,
                RecordUsage: false, NamedActions: [new("Switch", Switch, "\uE8A7", "Enter", KeepsPaletteOpen: true),
                    new("Close window", () => Change(info, owner, h => PostMessageW(h, 0x0010, 0, 0)), "\uE711", "Ctrl+Enter"),
                    new("Minimize", () => Change(info, owner, h => ShowWindowAsync(h, 6)), "\uE921"),
                    new("Maximize", () => Change(info, owner, h => ShowWindowAsync(h, 3)), "\uE922"),
                    new("Copy title", () => copy(info.Title), "\uE8C8")]));
        }
        Debug.WriteLine($"FlowDesk palette windows {items.Count} in {Stopwatch.GetElapsedTime(started).TotalMilliseconds:F1} ms");
        return items.ToArray();
    }

    /// <summary>"Microsoft Edge" instead of "msedge": the exe's display name, cached per exe path.</summary>
    private string FriendlyName(WindowInfo info)
    {
        // AUMID names derive from the window title, so only exe-based names are cacheable.
        if (!string.IsNullOrEmpty(info.Aumid) || string.IsNullOrEmpty(info.ExePath)) return WorkspaceCapture.DisplayName(info);
        lock (namesGate)
        {
            if (cachedNames.TryGetValue(info.ExePath, out string? cached)) return cached;
        }
        string name = WorkspaceCapture.DisplayName(info);
        lock (namesGate)
        {
            if (cachedNames.Count >= MaxCachedNames) cachedNames.Clear();
            cachedNames[info.ExePath] = name;
        }
        return name;
    }

    private void SwitchTo(WindowInfo info, uint owner)
    {
        if (!IsSameWindow(info, owner)) { hide(); notify("That window is no longer available."); return; }
        bool moved = true;
        if (services.Desktops.IsWindowOnCurrent(info.Handle) == false)
            moved = services.Desktops.GetWindowDesktopNumber(info.Handle) is int desktop && services.Desktops.GoTo(desktop);
        // Keep the palette foreground until the attempt; hiding first hands the permission back to the shell.
        bool switched = moved && IsSameWindow(info, owner) && (ops.Activate(info.Handle) || GetForegroundWindow() == (nint)info.Handle);
        hide();
        if (!switched) notify("Could not switch to that window. It may be on another desktop or running as administrator.");
    }

    private Task Change(WindowInfo info, uint owner, Func<nint, bool> operation)
    {
        if (!IsSameWindow(info, owner) || !operation((nint)info.Handle)) notify("Could not change that window.");
        return Task.CompletedTask;
    }

    private static bool IsSameWindow(WindowInfo info, uint owner) => IsWindow((nint)info.Handle) &&
        GetWindowThreadProcessId((nint)info.Handle, out uint actual) != 0 && actual == owner;

    [DllImport("user32.dll", SetLastError = true)] private static extern bool PostMessageW(nint hwnd, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern bool ShowWindowAsync(nint hwnd, int show);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint process);
}
