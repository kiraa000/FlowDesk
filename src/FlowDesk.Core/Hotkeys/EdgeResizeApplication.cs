using FlowDesk.Core.Model;

namespace FlowDesk.Core.Hotkeys;

/// <summary>Bounded transaction: apply, read every frame, one shared-line correction, otherwise restore snapshot.</summary>
public static class EdgeResizeApplication
{
    public static bool Apply(IWindowOps windows, EdgeResizePlan plan, Action<long>? clearSnap = null,
        PixelRect? workArea = null, long draggedHandle = 0)
    {
        if (plan.Frames.Count == 0) return true;
        foreach (var w in plan.Windows.Values)
        {
            if (windows.GetEdgeWindow(w.Handle) is not { } now || now.Fixed != w.Fixed || now.Insets != w.Insets || now.Scale != w.Scale ||
                (w.Handle != draggedHandle && now.Frame != w.Frame) ||
                (workArea is { } area && (windows.GetMetrics(w.Handle).WorkArea != area || !EdgeResize.SameMonitor(now.Frame, area)))) return false;
        }
        var touched = plan.Frames.Keys.ToHashSet();
        foreach (long h in touched) clearSnap?.Invoke(h);
        Move(windows, plan, plan.Frames);
        var actual = Read(windows, touched);
        if (Matches(plan.Frames, actual)) return true;
        if (actual.Count != touched.Count || plan.Lines.Count == 0) return Rollback();
        var corrected = new List<EdgeLine>();
        foreach (var line in plan.Lines)
        {
            int delta = line.Delta;
            int? correctionDelta = null;
            foreach (var m in line.Members.Where(m => touched.Contains(m.Window.Handle)))
            {
                var observed = actual[m.Window.Handle];
                var target = plan.Frames[m.Window.Handle];
                int wanted = EdgeResize.Coordinate(target, m.Edge);
                int got = EdgeResize.Coordinate(observed, m.Edge);
                if (got == wanted) continue;
                int start = EdgeResize.Coordinate(m.Window.Frame, m.Edge);
                // Refusal pins the complete transaction. Never force the other windows through it.
                if (got == start) return Rollback();
                int achieved = got - m.Baseline;
                if (Math.Sign(achieved) != Math.Sign(line.Delta) || achieved < line.MinimumDelta || achieved > line.MaximumDelta) return Rollback();
                if (correctionDelta is { } other && other != achieved) return Rollback();
                correctionDelta = achieved;
                delta = achieved;
            }
            corrected.Add(line with { Delta = delta });
        }
        var correction = EdgeResize.FromLines(corrected);
        if (correction.Frames.Any(p =>
        {
            var w = correction.Windows[p.Key];
            return p.Value.Width < Math.Min(w.Frame.Width, Math.Max((int)Math.Ceiling(200 * w.Scale), w.MinimumWidth)) ||
                p.Value.Height < Math.Min(w.Frame.Height, Math.Max((int)Math.Ceiling(120 * w.Scale), w.MinimumHeight));
        })) return Rollback();
        // Include unchanged snapshot members too: a correction to zero must undo the first batch.
        var targets = touched.ToDictionary(h => h, h => correction.Frames.GetValueOrDefault(h, plan.Windows[h].Frame));
        Move(windows, plan, targets);
        return Matches(targets, Read(windows, touched)) || Rollback();

        bool Rollback()
        {
            Move(windows, plan, touched.ToDictionary(h => h, h => plan.Windows[h].Frame));
            // Read back the rollback as well; false signals that native recovery is best-effort.
            _ = Read(windows, touched);
            return false;
        }
    }

    private static Dictionary<long, PixelRect> Read(IWindowOps windows, IEnumerable<long> handles)
    {
        var result = new Dictionary<long, PixelRect>();
        foreach (long h in handles) if (windows.GetEdgeWindow(h) is { } w) result[h] = w.Frame;
        return result;
    }
    private static bool Matches(IReadOnlyDictionary<long, PixelRect> wanted, IReadOnlyDictionary<long, PixelRect> actual) =>
        wanted.All(p => actual.TryGetValue(p.Key, out var r) && r == p.Value);
    private static void Move(IWindowOps windows, EdgeResizePlan plan, IReadOnlyDictionary<long, PixelRect> frames)
    {
        var moves = frames.Select(p => new KeyValuePair<long, PixelRect>(p.Key, plan.Windows[p.Key].Insets.Outer(p.Value)))
            .OrderBy(p => (long)p.Value.Width * p.Value.Height - (long)windows.GetMetrics(p.Key).Rect.Width * windows.GetMetrics(p.Key).Rect.Height).ToArray();
        windows.MoveBatch(moves);
    }
}
