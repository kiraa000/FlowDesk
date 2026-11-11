using FlowDesk.Core.Model;

namespace FlowDesk.Core.Windows;

/// <summary>
/// Maps saved window rectangles onto the monitors that exist now. Pure functions:
/// the display setup may have changed (monitor unplugged, resolution or scaling changed).
/// </summary>
public static class DisplayMapper
{
    /// <summary>Where a saved window should go on the current displays (outer rect, screen pixels).</summary>
    public static PixelRect Map(SavedWindow saved, IReadOnlyList<MonitorInfo> monitors)
    {
        if (monitors.Count == 0) return saved.Rect;

        // Same monitor, same geometry: the saved rect is exact.
        var same = monitors.FirstOrDefault(m => m.Device == saved.MonitorDevice && m.Bounds == saved.MonitorBounds && m.WorkArea == saved.MonitorWorkArea);
        if (same is not null) return KeepVisible(saved.Rect, same.WorkArea);

        // Otherwise pick a target monitor and scale the rect from the old work area into its work area.
        var target = monitors.FirstOrDefault(m => m.Device == saved.MonitorDevice)
                     ?? BestOverlap(saved.Rect, monitors)
                     ?? monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];
        var from = saved.MonitorWorkArea.IsEmpty ? target.WorkArea : saved.MonitorWorkArea;
        return KeepVisible(Scale(saved.Rect, from, target.WorkArea), target.WorkArea);
    }

    /// <summary>The monitor a rect mostly lies on (null if it lies on none).</summary>
    public static MonitorInfo? BestOverlap(PixelRect r, IReadOnlyList<MonitorInfo> monitors)
    {
        MonitorInfo? best = null;
        long bestArea = 0;
        foreach (var m in monitors)
        {
            long a = r.Intersect(m.Bounds).Area;
            if (a > bestArea) { bestArea = a; best = m; }
        }
        return best;
    }

    public static PixelRect Scale(PixelRect r, PixelRect from, PixelRect to)
    {
        double sx = (double)to.Width / Math.Max(1, from.Width), sy = (double)to.Height / Math.Max(1, from.Height);
        return new PixelRect(
            to.X + (int)Math.Round((r.X - from.X) * sx),
            to.Y + (int)Math.Round((r.Y - from.Y) * sy),
            Math.Max(1, (int)Math.Round(r.Width * sx)),
            Math.Max(1, (int)Math.Round(r.Height * sy)));
    }

    /// <summary>
    /// Shrinks the rect to fit the work area (allowing small invisible borders to overhang)
    /// and moves it so the whole window is reachable, title bar included.
    /// </summary>
    public static PixelRect KeepVisible(PixelRect r, PixelRect work, int borderSlack = 16)
    {
        int maxW = work.Width + 2 * borderSlack, maxH = work.Height + borderSlack;
        int w = Math.Min(r.Width, maxW), h = Math.Min(r.Height, maxH);
        int x = Math.Clamp(r.X, work.X - borderSlack, work.Right + borderSlack - w);
        int y = Math.Clamp(r.Y, work.Y, Math.Max(work.Y, work.Bottom + borderSlack - h));
        return new PixelRect(x, y, w, h);
    }

    /// <summary>Converts a layout fraction of a work area into a VISIBLE-frame pixel rect.</summary>
    public static PixelRect FromFraction(FractionRect f, PixelRect work) => new(
        work.X + (int)Math.Round(f.X * work.Width),
        work.Y + (int)Math.Round(f.Y * work.Height),
        (int)Math.Round(f.W * work.Width),
        (int)Math.Round(f.H * work.Height));

    /// <summary>Adds a window's invisible borders so its VISIBLE frame lands exactly on <paramref name="visible"/>.</summary>
    public static PixelRect ToOuter(PixelRect visible, (int L, int T, int R, int B) b) =>
        new(visible.X - b.L, visible.Y - b.T, visible.Width + b.L + b.R, visible.Height + b.T + b.B);
}
