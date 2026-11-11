using FlowDesk.Core.Model;
using FlowDesk.Core.Restore;
using FlowDesk.Core.Windows;

namespace FlowDesk.Core.Tests;

internal sealed class FakeWindowSystem : IWindowSystem
{
    public List<WindowInfo> Windows { get; } = [];
    public List<MonitorInfo> Monitors { get; } =
    [
        new("\\\\.\\DISPLAY1", new(0, 0, 1920, 1080), new(0, 0, 1920, 1040), true, 96),
    ];
    public List<(long Handle, PixelRect Rect, WindowShow Show)> Placed { get; } = [];
    public List<long> Closed { get; } = [];
    public List<long> Minimized { get; } = [];
    public List<long> Fronted { get; } = [];
    public long? Activated { get; private set; }
    public HashSet<long> Denied { get; } = [];
    public int OwnProcessId => 1;

    private long _next = 100;

    public WindowInfo Add(string exe, string title, string cls = "Win", WindowShow show = WindowShow.Normal, string? aumid = null, int pid = 0, string? args = null)
    {
        var w = new WindowInfo
        {
            Handle = _next++, ProcessId = pid == 0 ? (int)_next + 1000 : pid, ExePath = exe, Aumid = aumid, Arguments = args, Title = title,
            ClassName = cls, NormalRect = new(10, 10, 800, 600), Show = show, MonitorDevice = Monitors[0].Device,
        };
        Windows.Add(w);
        return w;
    }

    public IReadOnlyList<WindowInfo> GetAppWindows() => Windows.ToList();
    public WindowInfo? Get(long handle) => Windows.FirstOrDefault(w => w.Handle == handle);
    public bool IsAlive(long handle) => Windows.Any(w => w.Handle == handle);
    public IReadOnlyList<MonitorInfo> GetMonitors() => Monitors;

    public PlaceOutcome Place(long handle, PixelRect rect, WindowShow show)
    {
        int i = Windows.FindIndex(w => w.Handle == handle);
        if (i < 0) return PlaceOutcome.WindowGone;
        if (Denied.Contains(handle)) return PlaceOutcome.AccessDenied;
        Placed.Add((handle, rect, show));
        Windows[i] = Windows[i] with { NormalRect = rect, Show = show };
        return PlaceOutcome.Ok;
    }

    public (int L, int T, int R, int B) GetInvisibleBorders(long handle) => (7, 0, 7, 7);
    public void BringToFront(long handle) => Fronted.Add(handle);
    public bool Activate(long handle) { Activated = handle; return true; }
    public void Close(long handle) => Closed.Add(handle);
    public void Minimize(long handle)
    {
        Minimized.Add(handle);
        int i = Windows.FindIndex(w => w.Handle == handle);
        if (i >= 0) Windows[i] = Windows[i] with { Show = WindowShow.Minimized };
    }
}

/// <summary>Launcher whose "apps" show their window after a number of polls.</summary>
internal sealed class FakeLauncher(FakeWindowSystem sys) : IAppLauncher
{
    public List<string> Launched { get; } = [];
    public Dictionary<string, (string Exe, string Title, int AfterPolls)> Behaviour { get; } = [];
    public HashSet<string> Failing { get; } = [];
    /// <summary>Launches that open SEVERAL windows (browser session restore): item id -> windows.</summary>
    public Dictionary<string, List<(string Exe, string Title, int AfterPolls, string? Args)>> Opens { get; } = [];
    private readonly List<(int Due, string Exe, string Title, string? Args)> _queue = [];
    private int _polls;

    public LaunchResult Launch(WorkspaceItem item)
    {
        Launched.Add(item.Id);
        if (Failing.Contains(item.Id)) return LaunchResult.Fail("Program not found");
        if (Behaviour.TryGetValue(item.Id, out var b)) _queue.Add((_polls + b.AfterPolls, b.Exe, b.Title, null));
        if (Opens.TryGetValue(item.Id, out var many))
            foreach (var o in many) _queue.Add((_polls + o.AfterPolls, o.Exe, o.Title, o.Args));
        return new LaunchResult(true, HostExe: b.Exe);
    }

    /// <summary>Delay hook for the restorer: each poll may make queued windows appear.</summary>
    public Task Delay(TimeSpan _, CancellationToken __)
    {
        _polls++;
        foreach (var q in _queue.Where(q => q.Due <= _polls).ToList())
        {
            sys.Add(q.Exe, q.Title, args: q.Args);
            _queue.Remove(q);
        }
        return Task.CompletedTask;
    }
}

internal static class Build
{
    public static WorkspaceItem App(string exe, string title, PixelRect? rect = null, WindowShow show = WindowShow.Normal, int z = 0, string? aumid = null) => new()
    {
        Kind = ItemKind.App, Name = Path.GetFileNameWithoutExtension(exe), App = new AppRef { ExePath = exe, Aumid = aumid },
        Window = new SavedWindow
        {
            Rect = rect ?? new(100, 100, 900, 700), Show = show, MonitorDevice = "\\\\.\\DISPLAY1",
            MonitorBounds = new(0, 0, 1920, 1080), MonitorWorkArea = new(0, 0, 1920, 1040), Title = title, ZOrder = z,
        },
    };

    /// <summary>Options with no real waiting; the timeout is expressed through the poll count.</summary>
    public static RestoreOptions Fast(int maxPolls = 20) => new()
    {
        LaunchTimeout = TimeSpan.FromSeconds(maxPolls), PollInterval = TimeSpan.FromSeconds(1), SettleDelay = TimeSpan.Zero,
    };
}
