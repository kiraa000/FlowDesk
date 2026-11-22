using FlowDesk.Core.Desktops;
using FlowDesk.Core.Model;

namespace FlowDesk.Core.Hotkeys;

/// <summary>
/// Executes one binding through <see cref="IWindowOps"/> and <see cref="IVirtualDesktops"/>, and owns
/// the pause state. Runs on the host's single serialized worker thread; every delay is cancellable.
/// </summary>
public sealed class WindowShortcutActions(
    IWindowOps windows,
    IVirtualDesktops? desktops,
    IActionDelay delay)
{
    public bool IsPaused { get; private set; }
    public bool ResizeTouchingWindowsTogether { get; set; } = true;
    private readonly SnapCycle _snap = new();
    public WindowSearchSession Search { get; } = new(windows, desktops);

    /// <summary>Flips pause and returns the new state.</summary>
    public bool TogglePause()
    {
        SetPaused(!IsPaused);
        return IsPaused;
    }

    public void SetPaused(bool paused)
    {
        IsPaused = paused;
        Search.Reset();
    }

    /// <summary>Runs one hotkey binding. Pause gates only bindings marked pausable.</summary>
    public void Execute(HotkeyBinding binding, CancellationToken token)
    {
        if (token.IsCancellationRequested) return;
        if (binding.Id == "maximize-toggle") Search.Reset();
        if (IsPaused && binding.Pausable) return;

        if (binding.IsDesktop)
        {
            if (binding.Id.StartsWith("desktop-move-", StringComparison.Ordinal)) MoveToDesktop(binding.DesktopNumber, token);
            else SwitchDesktop(binding.DesktopNumber, token);
            return;
        }

        if (HotkeyCatalog.TryGetSnapDirection(binding, out var snapDirection))
        {
            Snap(snapDirection, token);
            return;
        }
        if (HotkeyCatalog.TryGetFocusDirection(binding, out _)) return; // multi-press is handled by the host
        if (binding.Id.StartsWith("edge-resize-", StringComparison.Ordinal) && binding.Direction is { } edgeDirection)
        {
            ResizeEdge(edgeDirection, token);
            return;
        }

        switch (binding.Id)
        {
            case "maximize-toggle": ToggleMaximize(token); break;
            case "minimize": Minimize(); break;
            case "next-window": NextWindow(token); break;
            case "center": Center(token); break;
            case "resize-wheel": ResizeWindow(grow: true, token); break;
        }
    }

    /// <summary>Runs the accumulated directional focus after the multi-press timer fires.</summary>
    public void Focus(WindowDirection direction, int steps, CancellationToken token)
    {
        if (token.IsCancellationRequested || IsPaused) return;
        long active = ActiveWindow();
        if (active == 0) return;
        var usable = DirectionalFocus.Usable(windows.GetWindowsInZOrder(), active);
        if (usable.Count == 0) return;
        var metrics = windows.GetMetrics(active);
        if (!metrics.Exists) return;
        if (DirectionalFocus.Pick(metrics.Rect, usable, direction, steps) is long target) windows.Activate(target);
    }

    /// <summary>Alt+Shift+Wheel: grow or shrink the active window, keeping it on its work area.</summary>
    public void ResizeWindow(bool grow, CancellationToken token)
    {
        if (token.IsCancellationRequested) return;
        long handle = ActiveWindow();
        if (handle == 0) return;
        _snap.Clear(handle);
        var metrics = windows.GetMetrics(handle);
        if (!metrics.Exists) return;
        if (metrics.IsMaximized)
        {
            windows.Restore(handle);
            if (!delay.Wait(TimeSpan.FromMilliseconds(25), token)) return;
            metrics = windows.GetMetrics(handle);
            if (!metrics.Exists) return;
        }
        else if (metrics.IsMinimized)
        {
            return;
        }
        if (ResizeTouchingWindowsTogether && windows.GetEdgeWindow(handle) is { } active)
        {
            var plan = EdgeResize.Wheel(active, metrics.WorkArea, EdgeCandidates(active), grow);
            if (plan.Lines.Count == 0)
            {
                if (plan.OuterRects.TryGetValue(handle, out var target)) windows.Move(handle, target);
            }
            else EdgeResizeApplication.Apply(windows, plan, _snap.Clear, metrics.WorkArea);
        }
        else windows.Move(handle, WindowGeometry.Resize(metrics.WorkArea, metrics.Rect, grow));
    }

    public void ResizeEdge(WindowDirection direction, CancellationToken token)
    {
        if (token.IsCancellationRequested || IsPaused) return;
        long h = ActiveWindow();
        if (h == 0 || windows.GetEdgeWindow(h) is not { } active) return;
        var area = windows.GetMetrics(h).WorkArea;
        EdgeResizeApplication.Apply(windows, EdgeResize.Keyboard(active, area, EdgeCandidates(active), direction, ResizeTouchingWindowsTogether), _snap.Clear, area);
    }

    private IReadOnlyList<EdgeWindow> EdgeCandidates(EdgeWindow active) =>
        DirectionalFocus.Usable(windows.GetWindowsInZOrder(), active.Handle)
            .Where(w => desktops is not { IsAvailable: true } || desktops.IsWindowOnCurrent(w.Handle) == true)
            .Select(w => windows.GetEdgeWindow(w.Handle)).OfType<EdgeWindow>().ToArray();

    public sealed record DragSnapshot(EdgeWindow Active, PixelRect Area, IReadOnlyList<EdgeLine> Lines);
    public DragSnapshot? BeginResizeDrag(long handle, PixelRect? initialFrame = null)
    {
        if (IsPaused || !ResizeTouchingWindowsTogether || windows.GetEdgeWindow(handle) is not { } active) return null;
        // The same candidate rules apply to the active drag window (including FlowDesk-owned windows).
        if (!DirectionalFocus.Usable(windows.GetWindowsInZOrder(), 0).Any(w => w.Handle == handle)) return null;
        var area = windows.GetMetrics(handle).WorkArea;
        if (initialFrame is { } frame)
        {
            if (!EdgeResize.SameMonitor(frame, area)) return null;
            active = active with { Frame = frame };
        }
        var candidates = EdgeCandidates(active);
        return new(active, area, Enum.GetValues<WindowDirection>().Select(e => EdgeResize.Line(active, area, candidates, e, 0)).ToArray());
    }

    /// <summary>END-only follow: no location-event backlog and no competition with the shell's live Snap group.</summary>
    public void EndResizeDrag(DragSnapshot snapshot)
    {
        if (IsPaused || !ResizeTouchingWindowsTogether || windows.GetEdgeWindow(snapshot.Active.Handle) is not { } active) return;
        if (windows.GetMetrics(active.Handle).WorkArea != snapshot.Area || active.Insets != snapshot.Active.Insets || active.Scale != snapshot.Active.Scale ||
            EdgeResize.DragEdge(snapshot.Active.Frame, active.Frame) is not { } edge) return;
        var original = snapshot.Lines.Single(l => l.Edge == edge);
        if (!original.Members.Any(m => m.Edge == EdgeResize.Opposite(edge))) return;
        // Abort stale snapshots; windows that disappeared, maximized, moved desktops or monitors must not be moved.
        var usable = DirectionalFocus.Usable(windows.GetWindowsInZOrder(), 0).Select(w => w.Handle).ToHashSet();
        int delta = EdgeResize.Coordinate(active.Frame, edge) - original.Origin;
        bool shellFollowed = false;
        foreach (var m in original.Members.Where(m => m.Window.Handle != active.Handle))
        {
            if (!usable.Contains(m.Window.Handle) || windows.GetEdgeWindow(m.Window.Handle) is not { } now ||
                !EdgeResize.SameMonitor(now.Frame, snapshot.Area)) return;
            var expected = EdgeResize.WithEdge(m.Window.Frame, m.Edge, m.Baseline + delta);
            if (now.Frame != m.Window.Frame)
            {
                if (EdgeResize.WithEdge(now.Frame, m.Edge, EdgeResize.Coordinate(expected, m.Edge)) != expected ||
                    Math.Abs(EdgeResize.Coordinate(now.Frame, m.Edge) - EdgeResize.Coordinate(expected, m.Edge)) > EdgeResize.Tolerance(m.Window)) return;
                shellFollowed = true;
            }
        }
        // Windows owns this group. Do not fight its grid or take over a partially followed group.
        if (shellFollowed) return;
        var planLine = EdgeResize.Line(snapshot.Active, snapshot.Area, original.Members.Where(m => m.Window.Handle != active.Handle).Select(m => m.Window).ToArray(), edge, delta);
        var plan = EdgeResize.FromLines([planLine]);
        var frames = plan.Frames.ToDictionary();
        // A pinned line has no planned changes relative to the START snapshot, but the user's
        // dragged window has already moved. It still has to return to the pinned edge.
        frames[active.Handle] = EdgeResize.WithEdge(snapshot.Active.Frame, edge, planLine.Origin + planLine.Delta);
        EdgeResizeApplication.Apply(windows, plan with { Frames = frames }, _snap.Clear, snapshot.Area, active.Handle);
    }

    public void DesktopWheel(bool previous, bool move, CancellationToken token)
    {
        if (desktops is not { IsAvailable: true } || token.IsCancellationRequested) return;
        if (desktops.GetCount() is not int count || desktops.GetCurrent() is not int current) return;
        count = Math.Max(1, count);
        int target = (current + (previous ? -1 : 1) + count) % count;
        if (move) MoveToDesktop(target + 1, token);
        else SwitchDesktop(target + 1, token);
    }

    private void SwitchDesktop(int number, CancellationToken token)
    {
        Search.Reset();
        if (desktops is not { IsAvailable: true }) return;
        if (desktops.GetCurrent() == number - 1) return;
        if (!TryEnsureDesktops(number, token) || token.IsCancellationRequested) return;
        if (!desktops.GoTo(number - 1)) return;
        if (!delay.Wait(TimeSpan.FromMilliseconds(100), token)) return;
        var candidates = windows.GetWindowsInZOrder().Where(w => w.IsVisible && !w.IsToolWindow &&
            !string.IsNullOrWhiteSpace(w.Title) && !windows.IsShellWindow(w.Handle) &&
            w.ClassName is not ("Progman" or "WorkerW") && desktops.IsWindowOnCurrent(w.Handle) == true &&
            windows.GetMetrics(w.Handle).Exists).ToList();
        var next = candidates.FirstOrDefault(w => !windows.GetMetrics(w.Handle).IsMinimized) ?? candidates.FirstOrDefault();
        if (next is not null)
        {
            if (windows.GetMetrics(next.Handle).IsMinimized) windows.Restore(next.Handle);
            windows.Activate(next.Handle);
        }
    }

    private void MoveToDesktop(int number, CancellationToken token)
    {
        Search.Reset();
        long active = ActiveWindow();
        if (active == 0) return;
        _snap.Clear(active);
        if (!TryEnsureDesktops(number, token) || token.IsCancellationRequested) return;
        if (!desktops!.MoveWindow(active, number - 1)) return;
        if (token.IsCancellationRequested) return;
        if (!desktops.GoTo(number - 1)) return;
        if (!delay.Wait(TimeSpan.FromMilliseconds(100), token)) return;
        var metrics = windows.GetMetrics(active);
        if (!metrics.Exists) return;
        if (metrics.IsMinimized) windows.Restore(active);
        windows.Activate(active);
    }

    /// <summary>Creates desktops until <paramref name="number"/> exist; stops on the first failure.</summary>
    private bool TryEnsureDesktops(int number, CancellationToken token)
    {
        if (desktops is not { IsAvailable: true }) return false;
        int? count = desktops.GetCount();
        if (count is null) return false;
        int guard = 0;
        while (count < number && guard++ < number)
        {
            if (token.IsCancellationRequested) return false;
            int? next = desktops.Create();
            if (next is null || next <= count) return false;
            count = next;
        }
        return count >= number;
    }

    private void Snap(WindowDirection direction, CancellationToken token)
    {
        long handle = ActiveWindow();
        if (handle == 0) return;
        var metrics = windows.GetMetrics(handle);
        if (!metrics.Exists) return;
        if (metrics.IsMinimized || metrics.IsMaximized)
        {
            windows.Restore(handle);
            if (!delay.Wait(TimeSpan.FromMilliseconds(25), token)) return;
            metrics = windows.GetMetrics(handle);
            if (!metrics.Exists) return;
        }
        int step = _snap.Next(handle, direction, metrics.Rect, metrics.WorkArea);
        windows.Move(handle, WindowGeometry.Snap(metrics.WorkArea, direction, step));
        var observed = windows.GetMetrics(handle);
        if (observed.Exists) _snap.Remember(handle, direction, step, observed.Rect, observed.WorkArea);
    }

    private void Center(CancellationToken token)
    {
        long handle = ActiveWindow();
        if (handle == 0) return;
        _snap.Clear(handle);
        var metrics = windows.GetMetrics(handle);
        if (!metrics.Exists) return;
        if (metrics.IsMinimized || metrics.IsMaximized)
        {
            windows.Restore(handle);
            if (!delay.Wait(TimeSpan.FromMilliseconds(30), token)) return;
            metrics = windows.GetMetrics(handle);
            if (!metrics.Exists) return;
        }
        windows.Move(handle, WindowGeometry.Center(metrics.WorkArea, metrics.Rect));
    }

    private void ToggleMaximize(CancellationToken token)
    {
        if (token.IsCancellationRequested) return;
        long handle = ActiveWindow();
        if (handle == 0) return;
        _snap.Clear(handle);
        var metrics = windows.GetMetrics(handle);
        if (!metrics.Exists) return;
        if (metrics.IsMaximized) windows.Restore(handle);
        else windows.Maximize(handle);
    }

    private void Minimize()
    {
        long handle = ActiveWindow();
        if (handle != 0) windows.Minimize(handle);
    }

    private void NextWindow(CancellationToken token)
    {
        long active = ActiveWindow();
        if (active == 0) return;
        var usable = DirectionalFocus.Usable(windows.GetWindowsInZOrder(), active);
        long next = usable.Count > 0 ? usable[0].Handle : 0;
        windows.Minimize(active);
        if (next == 0) return;
        if (!delay.Wait(TimeSpan.FromMilliseconds(40), token)) return;
        var metrics = windows.GetMetrics(next);
        if (!metrics.Exists) return;
        if (metrics.IsMinimized) windows.Restore(next);
        windows.Activate(next);
    }

    /// <summary>Foreground window, or 0 when there is none or it is a shell window.</summary>
    private long ActiveWindow()
    {
        long handle = windows.GetForeground();
        if (handle == 0 || windows.IsShellWindow(handle) || windows.IsOwnWindow(handle)) return 0;
        return windows.GetMetrics(handle).Exists ? handle : 0;
    }
}
