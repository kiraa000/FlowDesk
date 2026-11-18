using System.Text.Json;

namespace FlowDesk.Core.Palette;

public sealed record PaletteUsage(int Count, DateTimeOffset LastUsed);
public sealed class PaletteUsageStore(string path, int capacity = 256)
{
    private readonly object gate = new();
    private readonly SemaphoreSlim writer = new(1, 1);
    private Dictionary<string, PaletteUsage> entries = [];
    private const string RunPrefix = "run-history:";
    public IReadOnlyList<string> RunHistory()
    {
        lock (gate) return entries.Where(p => p.Key.StartsWith(RunPrefix, StringComparison.Ordinal))
            .OrderByDescending(p => p.Value.LastUsed).Take(20).Select(p => p.Key[RunPrefix.Length..]).ToArray();
    }
    public void RecordRun(string line, DateTimeOffset now)
    {
        lock (gate)
        {
            if (RunLineParser.Parse(line) is not { } parsed) return;
            entries[RunPrefix + parsed.Original] = new(1, now);
            foreach (var key in entries.Where(p => p.Key.StartsWith(RunPrefix, StringComparison.Ordinal)).OrderByDescending(p => p.Value.LastUsed).Skip(20).Select(p => p.Key).ToArray()) entries.Remove(key);
        }
    }
    public void DeleteRun(string line) { lock (gate) entries.Remove(RunPrefix + line); }
    public void ClearRunHistory()
    {
        lock (gate) foreach (string key in entries.Keys.Where(k => k.StartsWith(RunPrefix, StringComparison.Ordinal)).ToArray()) entries.Remove(key);
    }
    public PaletteUsage Get(string id) { lock (gate) return entries.GetValueOrDefault(id) ?? new(0, DateTimeOffset.MinValue); }
    public double Boost(string id) => Math.Min(40, Math.Log2(1.0 + Get(id).Count) * 6);
    public void Load()
    {
        Dictionary<string, PaletteUsage> next;
        try
        {
            next = Trim((JsonSerializer.Deserialize<Dictionary<string, PaletteUsage>>(File.ReadAllText(path)) ?? [])
                .Where(p => !string.IsNullOrWhiteSpace(p.Key) && p.Value is not null && p.Value.Count > 0)
                .ToDictionary(p => p.Key, p => p.Value with { Count = Math.Min(1_000_000, p.Value.Count) }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException) { next = []; }
        lock (gate) entries = next;
    }
    public void Record(string id, DateTimeOffset now)
    {
        lock (gate)
        {
            var old = entries.GetValueOrDefault(id);
            entries[id] = new(Math.Min(1_000_000, (old?.Count ?? 0) + 1), now);
            entries = Trim(entries);
        }
    }
    private Dictionary<string, PaletteUsage> Trim(Dictionary<string, PaletteUsage> values) => values
        .Where(p => !p.Key.StartsWith(RunPrefix, StringComparison.Ordinal)).OrderByDescending(p => p.Value.LastUsed).Take(Math.Max(1, capacity))
        .Concat(values.Where(p => p.Key.StartsWith(RunPrefix, StringComparison.Ordinal)).OrderByDescending(p => p.Value.LastUsed).Take(20)).ToDictionary();
    public async Task SaveAsync()
    {
        await writer.WaitAsync().ConfigureAwait(false);
        try
        {
            string json; lock (gate) json = JsonSerializer.Serialize(entries);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string tmp = path + ".tmp";
            await File.WriteAllTextAsync(tmp, json).ConfigureAwait(false);
            File.Move(tmp, path, true);
        }
        finally { writer.Release(); }
    }
}
