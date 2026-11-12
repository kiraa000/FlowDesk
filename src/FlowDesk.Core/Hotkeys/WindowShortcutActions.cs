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

    /// <summary>Flips pause and returns the new state.</summary>
    public bool TogglePause()
    {
        IsPaused = !IsPaused;
        return IsPaused;
    }

    public void SetPaused(bool paused) => IsPaused = paused;

    /// <summary>Runs one hotkey binding. Pause gates only bindings marked pausable.</summary>
    public void Execute(HotkeyBinding binding, CancellationToken token)
    {
        if (token.IsCancellationRequested) return;
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
        windows.Move(handle, WindowGeometry.Resize(metrics.WorkArea, metrics.Rect, grow));
    }

    private void SwitchDesktop(int number, CancellationToken token)
    {
        if (desktops is not { IsAvailable: true }) return;
        if (desktops.GetCurrent() == number - 1) return;
        if (!TryEnsureDesktops(number, token) || token.IsCancellationRequested) return;
        desktops.GoTo(number - 1);
    }

    private void MoveToDesktop(int number, CancellationToken token)
    {
        long active = ActiveWindow();
        if (active == 0) return;
        if (!TryEnsureDesktops(number, token) || token.IsCancellationRequested) return;
        if (!desktops!.MoveWindow(active, number - 1)) return;
        if (token.IsCancellationRequested) return;
        desktops.GoTo(number - 1);
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
        windows.Move(handle, WindowGeometry.Snap(metrics.WorkArea, direction));
    }

    private void Center(CancellationToken token)
    {
        long handle = ActiveWindow();
        if (handle == 0) return;
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
        if (handle == 0 || windows.IsShellWindow(handle)) return 0;
        return windows.GetMetrics(handle).Exists ? handle : 0;
    }
}
