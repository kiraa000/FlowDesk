namespace FlowDesk.Core.Hotkeys;

public readonly record struct ResumeBounds(int X, int Y, int Width, int Height);
public static class WindowResumeRules
{
    public static ResumeBounds OnMonitor(ResumeBounds normal, ResumeBounds target, IEnumerable<ResumeBounds> monitors)
    {
        var source = monitors.FirstOrDefault(m => normal.X >= m.X && normal.X < (long)m.X + m.Width && normal.Y >= m.Y && normal.Y < (long)m.Y + m.Height);
        if (source.Width == 0) source = target;
        return Bounds(target.X + normal.X - source.X, target.Y + normal.Y - source.Y, normal.Width, normal.Height, target, [target]);
    }
    public static string Page(string? saved) => saved is "Settings" or "Layouts" or "Advanced" or "Focus" or "Calm" or "Hotkeys" or "Capture" ? saved : "Workspaces";
    public static ResumeBounds Bounds(int? x, int? y, int width, int height, ResumeBounds primary, IEnumerable<ResumeBounds> monitors)
    {
        var area = primary;
        if (x is int sx && y is int sy)
            foreach (var monitor in monitors)
                if (sx >= monitor.X && sx < (long)monitor.X + monitor.Width && sy >= monitor.Y && sy < (long)monitor.Y + monitor.Height)
                { area = monitor; break; }
        width = Math.Clamp(width, 720, Math.Max(720, area.Width));
        height = Math.Clamp(height, 520, Math.Max(520, area.Height));
        return new(Math.Clamp(x ?? area.X + 80, area.X, Math.Max(area.X, area.X + area.Width - width)),
            Math.Clamp(y ?? area.Y + 80, area.Y, Math.Max(area.Y, area.Y + area.Height - height)), width, height);
    }
}
