namespace FlowDesk.Core.Windows;

/// <summary>Names for the current topology only; observations replace, rather than grow, the snapshot.</summary>
public sealed class MonitorNames
{
    private readonly object gate = new();
    private Dictionary<string, string> names = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> stableNames = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> unidentified = new(StringComparer.OrdinalIgnoreCase);
    public void Observe(IEnumerable<MonitorInfo> monitors)
    {
        var ordered = monitors.OrderBy(m => m.Bounds.X).ThenBy(m => m.Bounds.Y)
            .ThenBy(m => m.Device, StringComparer.OrdinalIgnoreCase).ToArray();
        var baseNames = ordered.Select((m, i) => !string.IsNullOrWhiteSpace(m.FriendlyName)
            ? m.FriendlyName.Trim() : m.IsInternal ? "Built-in display" : $"Display {i + 1}").ToArray();
        var counts = baseNames.GroupBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var next = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var stable = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < ordered.Length; i++)
        {
            string name = baseNames[i];
            seen.TryGetValue(name, out int occurrence); seen[name] = ++occurrence;
            string label = counts[name] > 1 ? $"{name} ({occurrence})" : name;
            next[ordered[i].Device] = label;
            if (!string.IsNullOrWhiteSpace(ordered[i].StableId)) stable.TryAdd(ordered[i].StableId, label);
        }
        var unknown = ordered.Where(m => string.IsNullOrWhiteSpace(m.StableId)).Select(m => m.Device).ToHashSet(StringComparer.OrdinalIgnoreCase);
        lock (gate) { names = next; stableNames = stable; unidentified = unknown; }
    }
    public string For(string device, string? stableId = null, string? savedName = null)
    {
        lock (gate)
        {
            if (!string.IsNullOrWhiteSpace(stableId))
            {
                if (stableNames.TryGetValue(stableId, out var stable)) return stable;
                if (unidentified.Contains(device) && names.TryGetValue(device, out var fallback)) return fallback;
                // A recycled GDI name is not evidence that the saved physical monitor is connected.
                return $"{(string.IsNullOrWhiteSpace(savedName) ? "Display" : savedName.Trim())} (not connected)";
            }
            return names.TryGetValue(device, out var name) ? name
                : $"{(string.IsNullOrWhiteSpace(savedName) ? "Display" : savedName.Trim())} (not connected)";
        }
    }
    public string Label(MonitorInfo monitor) =>
        $"{For(monitor.Device)} · {monitor.Bounds.Width} × {monitor.Bounds.Height}" + (monitor.IsPrimary ? " · Main" : "");
}
