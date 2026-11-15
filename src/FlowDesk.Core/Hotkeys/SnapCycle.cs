using FlowDesk.Core.Model;

namespace FlowDesk.Core.Hotkeys;

/// <summary>Remember the observed result, not the requested size: minimum-size constrained apps still cycle.</summary>
public sealed class SnapCycle
{
    private readonly Dictionary<long, (WindowDirection Direction, int Step, PixelRect Actual, PixelRect Area)> _states = [];

    public int Next(long handle, WindowDirection direction, PixelRect actual, PixelRect area)
    {
        if (!_states.TryGetValue(handle, out var state) || state.Direction != direction || state.Area != area ||
            !Near(state.Actual, actual)) return 0;
        return (state.Step + 1) % 3;
    }

    public void Remember(long handle, WindowDirection direction, int step, PixelRect actual, PixelRect area)
    {
        if (_states.Count > 1024) _states.Clear();
        _states[handle] = (direction, step, actual, area);
    }

    public void Clear(long handle) => _states.Remove(handle);

    private static bool Near(PixelRect a, PixelRect b) => Math.Abs((long)a.X - b.X) <= 4 &&
        Math.Abs((long)a.Y - b.Y) <= 4 && Math.Abs((long)a.Width - b.Width) <= 4 && Math.Abs((long)a.Height - b.Height) <= 4;
}
