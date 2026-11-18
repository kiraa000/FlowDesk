namespace FlowDesk.Core.Windows;

/// <summary>Position-based labels retained for one run. New screens append rather than renumbering existing screens.</summary>
public sealed class MonitorNames
{
    private readonly object gate = new();
    private readonly Dictionary<string, int> numbers = new(StringComparer.OrdinalIgnoreCase);
    public void Observe(IEnumerable<MonitorInfo> monitors)
    {
        lock (gate)
            foreach (var monitor in monitors.OrderBy(m => m.Bounds.X).ThenBy(m => m.Bounds.Y)
                         .ThenBy(m => m.Device, StringComparer.OrdinalIgnoreCase))
                if (!numbers.ContainsKey(monitor.Device)) numbers.Add(monitor.Device, numbers.Count + 1);
    }
    public string For(string device)
    {
        lock (gate) return numbers.TryGetValue(device, out int number) ? $"Display {number}" : "Display (not connected)";
    }
    public string Label(MonitorInfo monitor) =>
        $"{For(monitor.Device)} · {monitor.Bounds.Width} × {monitor.Bounds.Height}" + (monitor.IsPrimary ? " · Main" : "");
}
