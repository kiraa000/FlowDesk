using FlowDesk.Core.Model;
using FlowDesk.Core.Restore;
using FlowDesk.Core.Windows;

namespace FlowDesk.Core.Layout;

public enum LayoutPreset { TwoColumns, PrimarySidebar, Grid, Cascade }

/// <summary>Layout maths in fractions of a work area (ported from the Blazor demo). Pure.</summary>
public static class LayoutEngine
{
    /// <summary>Gap between tiled windows and around the edge, as a fraction of the work area width.</summary>
    public const double Gap = 0.006;

    /// <summary>One rect per window, in order; the first window is the "primary".</summary>
    public static IReadOnlyList<FractionRect> Compute(LayoutPreset preset, int count, double aspect = 16.0 / 9)
    {
        if (count <= 0) return [];
        double gx = Gap, gy = Gap * aspect; // same gap in pixels both ways
        return preset switch
        {
            LayoutPreset.TwoColumns => Columns(count, 0.5, gx, gy),
            LayoutPreset.PrimarySidebar => Columns(count, 0.68, gx, gy),
            LayoutPreset.Cascade => Cascade(count),
            _ => Grid(count, gx, gy),
        };
    }

    private static List<FractionRect> Columns(int n, double primary, double gx, double gy)
    {
        if (n == 1) return [new(gx, gy, 1 - 2 * gx, 1 - 2 * gy)];
        var list = new List<FractionRect> { new(gx, gy, primary - gx * 1.5, 1 - 2 * gy) };
        double rx = primary + gx * 0.5, rw = 1 - rx - gx;
        int stacked = n - 1;
        double h = (1 - gy * (stacked + 1)) / stacked;
        for (int i = 0; i < stacked; i++) list.Add(new(rx, gy + i * (h + gy), rw, h));
        return list;
    }

    private static List<FractionRect> Grid(int n, double gx, double gy)
    {
        int cols = n switch { 1 => 1, <= 4 => 2, <= 9 => 3, _ => 4 };
        int rows = (int)Math.Ceiling(n / (double)cols);
        double w = (1 - gx * (cols + 1)) / cols, h = (1 - gy * (rows + 1)) / rows;
        var list = new List<FractionRect>(n);
        for (int i = 0; i < n; i++)
            list.Add(new(gx + (i % cols) * (w + gx), gy + (i / cols) * (h + gy), w, h));
        return list;
    }

    private static List<FractionRect> Cascade(int n)
    {
        var list = new List<FractionRect>(n);
        for (int i = 0; i < n; i++)
        {
            int k = i % 7;
            list.Add(new(0.04 + k * 0.035, 0.04 + k * 0.05, 0.6, 0.65));
        }
        return list;
    }
}

/// <summary>Arranges a workspace's open windows on one monitor using a preset.</summary>
public sealed class LayoutApplier(IWindowSystem windows)
{
    /// <summary>
    /// Places <paramref name="ordered"/> windows (first = primary) on the monitor <paramref name="monitorDevice"/>
    /// (null = the monitor holding the first window). Visible frames line up exactly; invisible borders are accounted for.
    /// </summary>
    public IReadOnlyList<(long Handle, PlaceOutcome Outcome)> Apply(LayoutPreset preset, IReadOnlyList<WindowInfo> ordered, string? monitorDevice = null)
    {
        var monitors = windows.GetMonitors();
        if (ordered.Count == 0 || monitors.Count == 0) return [];
        var mon = monitors.FirstOrDefault(m => m.Device == (monitorDevice ?? ordered[0].MonitorDevice))
                  ?? monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];
        var rects = LayoutEngine.Compute(preset, ordered.Count, (double)mon.WorkArea.Width / Math.Max(1, mon.WorkArea.Height));

        var results = new List<(long, PlaceOutcome)>();
        for (int i = 0; i < ordered.Count; i++)
        {
            var w = ordered[i];
            var visible = DisplayMapper.FromFraction(rects[i], mon.WorkArea);
            // Measure borders in the normal state; a maximized window reports none.
            if (w.Show != WindowShow.Normal) windows.Place(w.Handle, w.NormalRect, WindowShow.Normal);
            var outer = DisplayMapper.ToOuter(visible, windows.GetInvisibleBorders(w.Handle));
            results.Add((w.Handle, windows.Place(w.Handle, outer, WindowShow.Normal)));
        }
        // Primary on top, then the rest in order.
        for (int i = ordered.Count - 1; i >= 0; i--) windows.BringToFront(ordered[i].Handle);
        return results;
    }
}
