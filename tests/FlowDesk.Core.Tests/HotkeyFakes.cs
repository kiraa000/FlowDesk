using FlowDesk.Core.Desktops;
using FlowDesk.Core.Hotkeys;
using FlowDesk.Core.Model;

namespace FlowDesk.Core.Tests;

internal sealed class FakeWindowOps : IWindowOps
{
    private long _next = 100;

    public long Foreground { get; set; }
    public HashSet<long> ShellHandles { get; } = [];
    public Dictionary<long, WindowMetrics> Metrics { get; } = [];
    public Dictionary<long, PixelRect> RestoredRects { get; } = [];
    public List<FocusWindow> Windows { get; } = [];
    public List<string> Log { get; } = [];
    public List<PixelRect> Moves { get; } = [];
    public HashSet<long> MoveFailures { get; } = [];
    public long? Activated { get; private set; }
    public int EnumerationCount { get; private set; }

    public long AddWindow(PixelRect rect, PixelRect? workArea = null, bool visible = true, bool cloaked = false,
        bool minimized = false, bool maximized = false, string title = "Window", string cls = "Win", bool tool = false)
    {
        long handle = _next++;
        Metrics[handle] = new WindowMetrics(rect, workArea ?? new PixelRect(0, 0, 1920, 1040), minimized, maximized, true);
        Windows.Add(new FocusWindow(handle, rect, Windows.Count, visible, cloaked, minimized, title, cls, tool));
        return handle;
    }

    public long GetForeground() => Foreground;

    public bool IsShellWindow(long handle) => ShellHandles.Contains(handle);

    public WindowMetrics GetMetrics(long handle) =>
        Metrics.TryGetValue(handle, out var metrics) ? metrics : default;

    public void Restore(long handle)
    {
        Log.Add($"restore:{handle}");
        if (!Metrics.TryGetValue(handle, out var metrics)) return;
        var rect = RestoredRects.TryGetValue(handle, out var restored) ? restored : metrics.Rect;
        Metrics[handle] = metrics with { Rect = rect, IsMinimized = false, IsMaximized = false };
    }

    public void Maximize(long handle)
    {
        Log.Add($"maximize:{handle}");
        if (Metrics.TryGetValue(handle, out var metrics)) Metrics[handle] = metrics with { IsMaximized = true };
    }

    public void Minimize(long handle)
    {
        Log.Add($"minimize:{handle}");
        if (Metrics.TryGetValue(handle, out var metrics)) Metrics[handle] = metrics with { IsMinimized = true };
    }

    public bool Move(long handle, PixelRect rect)
    {
        Log.Add($"move:{handle}:{rect.X},{rect.Y},{rect.Width},{rect.Height}");
        Moves.Add(rect);
        if (MoveFailures.Contains(handle)) return false;
        if (Metrics.TryGetValue(handle, out var metrics)) Metrics[handle] = metrics with { Rect = rect };
        return true;
    }

    public bool Activate(long handle)
    {
        Log.Add($"activate:{handle}");
        Activated = handle;
        Foreground = handle;
        return true;
    }

    public IReadOnlyList<FocusWindow> GetWindowsInZOrder() { EnumerationCount++; return Windows.ToList(); }
}

internal sealed class FakeVirtualDesktops : IVirtualDesktops
{
    public bool Available { get; set; } = true;
    public int Count { get; set; } = 1;
    public int Current { get; set; }
    public bool CreateFails { get; set; }
    public bool MoveFails { get; set; }
    /// <summary>Per-target failures, e.g. fail only the staging (current) or final (saved) move.</summary>
    public HashSet<int> FailMoveIndices { get; } = [];
    public int DisposeCount { get; private set; }
    /// <summary>When false, a successful MoveWindow does not change the observed desktop (unverified move).</summary>
    public bool ApplyMoves { get; set; } = true;
    /// <summary>When true, reads return null as a failing native backend would.</summary>
    public bool ReadsReturnNull { get; set; }
    public Action? OnGetCount { get; set; }
    public Action? OnCreate { get; set; }
    public Action? OnMoveWindow { get; set; }
    public List<string> Log { get; } = [];
    /// <summary>Desktop index requested per MoveWindow call.</summary>
    public List<int> Moves { get; } = [];
    public List<int> Gotos { get; } = [];
    public int CreateCalls { get; private set; }
    public Dictionary<long, int?> DesktopNumbers { get; } = [];
    public Dictionary<long, Guid?> DesktopIds { get; } = [];
    /// <summary>Unknown handles keep the old behaviour (on the current desktop).</summary>
    public Dictionary<long, bool?> OnCurrent { get; } = [];

    public bool IsAvailable => Available;
    public string Status => Available ? "Desktop shortcuts are ready." : "Unavailable.";
    public int? GetCount() { OnGetCount?.Invoke(); return ReadsReturnNull ? null : Count; }
    public int? GetCurrent() => ReadsReturnNull ? null : Current;

    public bool GoTo(int index)
    {
        Log.Add($"goto:{index}");
        Gotos.Add(index);
        Current = index;
        return true;
    }

    public bool MoveWindow(long handle, int index)
    {
        Log.Add($"movewindow:{handle}:{index}");
        Moves.Add(index);
        OnMoveWindow?.Invoke(); // deterministic hook, before the observed desktop changes
        if (MoveFails || FailMoveIndices.Contains(index)) return false;
        if (ApplyMoves) DesktopNumbers[handle] = index;
        return true;
    }

    public bool? IsWindowOnCurrent(long handle) => OnCurrent.TryGetValue(handle, out var known) ? known : true;

    public int? GetWindowDesktopNumber(long handle) =>
        ReadsReturnNull ? null : DesktopNumbers.GetValueOrDefault(handle);

    public Guid? GetWindowDesktopId(long handle) =>
        ReadsReturnNull ? null : DesktopIds.GetValueOrDefault(handle);

    public int? Create()
    {
        Log.Add("create");
        CreateCalls++;
        OnCreate?.Invoke();
        if (CreateFails) return null;
        Count++;
        return Count;
    }

    public void Dispose() => DisposeCount++;
}

internal sealed class RecordingDelay : IActionDelay
{
    public List<TimeSpan> Waits { get; } = [];
    public bool Cancelled { get; set; }

    public bool Wait(TimeSpan duration, CancellationToken token)
    {
        Waits.Add(duration);
        return !Cancelled && !token.IsCancellationRequested;
    }
}

internal sealed class FakeScheduler : IOneShotScheduler
{
    public List<FakeTimer> Timers { get; } = [];

    public IDisposable Schedule(TimeSpan delay, Action callback)
    {
        var timer = new FakeTimer(delay, callback);
        Timers.Add(timer);
        return timer;
    }
}

internal sealed class FakeTimer(TimeSpan delay, Action callback) : IDisposable
{
    public TimeSpan Delay { get; } = delay;
    public bool Disposed { get; private set; }

    public void Fire()
    {
        if (!Disposed) callback();
    }

    /// <summary>Runs the callback even after Dispose, proving stale timers are token-rejected.</summary>
    public void FireIgnoringDisposal() => callback();

    public void Dispose() => Disposed = true;
}
