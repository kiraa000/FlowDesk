using FlowDesk.Core.Desktops;

namespace FlowDesk.Core.Hotkeys;

/// <summary>Serialized worker state; the snapshot is frozen until release/reset. Restore history outlives a hold.</summary>
public sealed class WindowSearchSession(IWindowOps windows, IVirtualDesktops? desktops)
{
    private List<long>? _candidates;
    private readonly List<long> _stack = [];
    private readonly List<long> _minimized = [];
    private bool _upUsed;
    private int? _desktop;

    public void Reset()
    {
        _candidates = null;
        _stack.Clear();
        _upUsed = false;
    }

    public void Wheel(bool up)
    {
        if (desktops is null) return;
        int? current = desktops.GetCurrent();
        if (current != _desktop) Reset();
        if (_candidates is null)
        {
            _desktop = current;
            _candidates = windows.GetWindowsInZOrder().Where(w => w.IsVisible && !w.IsToolWindow &&
                !string.IsNullOrWhiteSpace(w.Title) && w.ClassName is not ("Progman" or "WorkerW") &&
                !windows.IsShellWindow(w.Handle) && Usable(w.Handle)).Select(w => w.Handle).Distinct().ToList();
            long active = windows.GetForeground();
            if (active != 0 && Usable(active) && !windows.IsShellWindow(active)) _stack.Add(active);
        }
        if (up)
        {
            _upUsed = true;
            while (_minimized.Count > 0)
            {
                long recent = _minimized[^1];
                _minimized.RemoveAt(_minimized.Count - 1);
                if (!Usable(recent) || !windows.GetMetrics(recent).IsMinimized) continue;
                Activate(recent);
                _stack.Remove(recent);
                _stack.Add(recent);
                return;
            }
            long next = _candidates.FirstOrDefault(h => !_stack.Contains(h) && Usable(h));
            if (next == 0) return;
            Activate(next);
            _stack.Add(next);
        }
        else if (_upUsed)
        {
            if (_stack.Count <= 1) return;
            long top = _stack[^1];
            _stack.RemoveAt(_stack.Count - 1);
            if (Usable(top)) windows.Minimize(top);
            while (_stack.Count > 0 && !Usable(_stack[^1])) _stack.RemoveAt(_stack.Count - 1);
            if (_stack.Count > 0) Activate(_stack[^1]);
        }
        else
        {
            long active = windows.GetForeground();
            if (!_candidates.Contains(active) || !Usable(active)) return;
            long next = _candidates.FirstOrDefault(h => h != active && Usable(h) && !windows.GetMetrics(h).IsMinimized);
            windows.Minimize(active);
            _minimized.Remove(active);
            _minimized.Add(active);
            if (next != 0) Activate(next);
            _stack.Clear();
            if (next != 0) _stack.Add(next);
        }
    }

    private bool Usable(long handle) => windows.GetMetrics(handle).Exists && desktops?.IsWindowOnCurrent(handle) == true;

    private void Activate(long handle)
    {
        if (windows.GetMetrics(handle).IsMinimized) windows.Restore(handle);
        windows.Activate(handle);
    }
}
