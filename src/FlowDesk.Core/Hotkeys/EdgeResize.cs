using FlowDesk.Core.Model;

namespace FlowDesk.Core.Hotkeys;

public readonly record struct FrameInsets(int Left, int Top, int Right, int Bottom)
{
    public PixelRect Outer(PixelRect frame) => PixelRect.FromLtrb(frame.X - Left, frame.Y - Top, frame.Right + Right, frame.Bottom + Bottom);
}

/// <summary>All geometry is in physical visible-frame pixels. A fixed window participates but pins its line.</summary>
public sealed record EdgeWindow(long Handle, PixelRect Frame, FrameInsets Insets = default,
    double Scale = 1, int MinimumWidth = 0, int MinimumHeight = 0, bool Fixed = false);

public sealed record EdgeMember(EdgeWindow Window, WindowDirection Edge, int Baseline);
public sealed record EdgeLine(WindowDirection Edge, int Origin, int Delta, IReadOnlyList<EdgeMember> Members,
    int MinimumDelta = int.MinValue, int MaximumDelta = int.MaxValue);
public sealed record EdgeResizePlan(IReadOnlyList<EdgeLine> Lines, IReadOnlyDictionary<long, EdgeWindow> Windows,
    IReadOnlyDictionary<long, PixelRect> Frames)
{
    public IReadOnlyDictionary<long, PixelRect> OuterRects => Frames.ToDictionary(p => p.Key, p => Windows[p.Key].Insets.Outer(p.Value));
}

/// <summary>Snapshot-only shared-border planner. No native calls, measurements or moves.</summary>
public static class EdgeResize
{
    public static int Coordinate(PixelRect r, WindowDirection edge) => edge switch
    { WindowDirection.Left => r.X, WindowDirection.Right => r.Right, WindowDirection.Up => r.Y, _ => r.Bottom };
    public static WindowDirection Opposite(WindowDirection e) => e switch
    { WindowDirection.Left => WindowDirection.Right, WindowDirection.Right => WindowDirection.Left, WindowDirection.Up => WindowDirection.Down, _ => WindowDirection.Up };
    public static bool Horizontal(WindowDirection e) => e is WindowDirection.Left or WindowDirection.Right;
    public static int Tolerance(EdgeWindow w) => Math.Max(1, (int)Math.Round(12 * w.Scale));
    public static int Step(PixelRect area, WindowDirection e) => Math.Max(1, (int)Math.Round((Horizontal(e) ? area.Width : area.Height) * .05, MidpointRounding.AwayFromZero));
    public static bool OnBorder(EdgeWindow w, PixelRect area, WindowDirection e) => Math.Abs(Coordinate(w.Frame, e) - Coordinate(area, e)) <= Tolerance(w);

    public static EdgeResizePlan Keyboard(EdgeWindow active, PixelRect area, IReadOnlyList<EdgeWindow> candidates,
        WindowDirection direction, bool together = true)
    {
        var edge = OnBorder(active, area, direction) ? Opposite(direction) : direction;
        int delta = Step(area, edge) * (direction is WindowDirection.Left or WindowDirection.Up ? -1 : 1);
        return FromLines([Line(active, area, candidates, edge, delta, together)]);
    }

    public static EdgeResizePlan Wheel(EdgeWindow active, PixelRect area, IReadOnlyList<EdgeWindow> candidates, bool grow)
    {
        if (active.Fixed) return FromLines([]);
        var lines = Enum.GetValues<WindowDirection>().Select(e => Line(active, area, candidates, e, 0, true)).ToArray();
        if (!lines.Any(l => l.Members.Any(m => m.Edge == Opposite(l.Edge))))
        {
            var frame = WindowGeometry.Resize(area, active.Frame, grow);
            return new([], new Dictionary<long, EdgeWindow> { [active.Handle] = active }, new Dictionary<long, PixelRect> { [active.Handle] = frame });
        }
        var moving = lines.Where(l => !OnBorder(active, area, l.Edge)).Select(l => Line(active, area, candidates, l.Edge,
            Step(area, l.Edge) * (l.Edge is WindowDirection.Left or WindowDirection.Up ? -1 : 1) * (grow ? 1 : -1), true)).ToArray();
        // Two inward edges can each satisfy a minimum separately yet violate it together.
        // Reduce the precomputed deltas jointly, never measure after moving a first line.
        while (true)
        {
            var plan = FromLines(moving);
            if (plan.Frames.All(p =>
            {
                var w = plan.Windows[p.Key];
                int minW = Math.Min(w.Frame.Width, Math.Max((int)Math.Ceiling(200 * w.Scale), w.MinimumWidth));
                int minH = Math.Min(w.Frame.Height, Math.Max((int)Math.Ceiling(120 * w.Scale), w.MinimumHeight));
                return p.Value.Width >= minW && p.Value.Height >= minH;
            })) return plan;
            moving = moving.Select(l => l with { Delta = l.Delta - Math.Sign(l.Delta) }).ToArray();
        }
    }

    public static EdgeLine Line(EdgeWindow active, PixelRect area, IReadOnlyList<EdgeWindow> candidates,
        WindowDirection edge, int requestedDelta, bool together = true)
    {
        int origin = Coordinate(active.Frame, edge), t = Tolerance(active);
        var members = new List<EdgeMember> { new(active, edge, origin) };
        if (together)
        {
            var pool = candidates.Where(w => w.Handle != active.Handle && SameMonitor(w.Frame, area)).Select(w =>
            {
                int own = Coordinate(w.Frame, edge), opposite = Coordinate(w.Frame, Opposite(edge));
                if (Math.Abs(opposite - origin) <= t) return new EdgeMember(w, Opposite(edge), opposite);
                if (Math.Abs(own - origin) <= t) return new EdgeMember(w, edge, own);
                return null;
            }).Where(m => m is not null).Cast<EdgeMember>().ToList();
            bool changed;
            do
            {
                changed = false;
                foreach (var m in pool.ToArray())
                {
                    if (!members.Any(a => Connected(a, m, edge, t))) continue;
                    members.Add(m); pool.Remove(m); changed = true;
                }
            } while (changed);
        }
        // Positive gaps are retained. Small visible overlaps are closed at the shared line.
        members = members.Select(m => m.Edge != edge && (m.Baseline - origin) * Sign(edge) < 0 ? m with { Baseline = origin } : m).ToList();
        int low = int.MinValue, high = int.MaxValue;
        foreach (var m in members)
        {
            var w = m.Window; var r = w.Frame;
            int min = Horizontal(edge) ? Math.Max((int)Math.Ceiling(200 * w.Scale), w.MinimumWidth) : Math.Max((int)Math.Ceiling(120 * w.Scale), w.MinimumHeight);
            // Do not force an already-undersized window larger as a side effect.
            min = Math.Min(min, Horizontal(edge) ? r.Width : r.Height);
            int far = Coordinate(r, Opposite(m.Edge));
            int borderLow = Horizontal(edge) ? area.X : area.Y, borderHigh = Horizontal(edge) ? area.Right : area.Bottom;
            if (m.Edge is WindowDirection.Left or WindowDirection.Up)
            { low = Math.Max(low, borderLow - m.Baseline); high = Math.Min(high, far - min - m.Baseline); }
            else
            { low = Math.Max(low, far + min - m.Baseline); high = Math.Min(high, borderHigh - m.Baseline); }
            if (w.Fixed) { low = Math.Max(low, 0); high = Math.Min(high, 0); }
        }
        int delta = low <= high ? Math.Clamp(requestedDelta, low, high) : 0;
        return new(edge, origin, delta, members, low, high);
    }

    private static int Sign(WindowDirection e) => e is WindowDirection.Left or WindowDirection.Up ? -1 : 1;
    public static bool SameMonitor(PixelRect frame, PixelRect area) => frame.X + frame.Width / 2.0 >= area.X && frame.X + frame.Width / 2.0 < area.Right && frame.Y + frame.Height / 2.0 >= area.Y && frame.Y + frame.Height / 2.0 < area.Bottom;
    private static bool Connected(EdgeMember a, EdgeMember b, WindowDirection edge, int t)
    {
        var x = a.Window.Frame; var y = b.Window.Frame;
        int overlap = Horizontal(edge) ? Math.Min(x.Bottom, y.Bottom) - Math.Max(x.Y, y.Y) : Math.Min(x.Right, y.Right) - Math.Max(x.X, y.X);
        return a.Edge != b.Edge ? overlap >= 50 : overlap >= -t;
    }

    public static PixelRect WithEdge(PixelRect r, WindowDirection e, int value) => e switch
    {
        WindowDirection.Left => PixelRect.FromLtrb(value, r.Y, r.Right, r.Bottom),
        WindowDirection.Right => PixelRect.FromLtrb(r.X, r.Y, value, r.Bottom),
        WindowDirection.Up => PixelRect.FromLtrb(r.X, value, r.Right, r.Bottom),
        _ => PixelRect.FromLtrb(r.X, r.Y, r.Right, value),
    };

    public static EdgeResizePlan FromLines(IReadOnlyList<EdgeLine> lines)
    {
        var windows = lines.SelectMany(l => l.Members).Select(m => m.Window).DistinctBy(w => w.Handle).ToDictionary(w => w.Handle);
        var frames = windows.ToDictionary(p => p.Key, p => p.Value.Frame);
        foreach (var l in lines.Where(l => l.Delta != 0))
            foreach (var m in l.Members) frames[m.Window.Handle] = WithEdge(frames[m.Window.Handle], m.Edge, m.Baseline + l.Delta);
        return new(lines, windows, frames.Where(p => p.Value != windows[p.Key].Frame).ToDictionary());
    }

    /// <summary>Only a single changed edge is a resize we can follow. Moves and corner drags are ignored.</summary>
    public static WindowDirection? DragEdge(PixelRect before, PixelRect after)
    {
        var changed = Enum.GetValues<WindowDirection>().Where(e => Coordinate(before, e) != Coordinate(after, e)).ToArray();
        return changed.Length == 1 ? changed[0] : null;
    }
}
