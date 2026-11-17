using System.Text.Json;

namespace FlowDesk.Core.Calm;

public sealed record CalmOriginal(RegistryAddress Address, RegistryValue? Previous, bool KeyWasMissing = false);
public sealed class CalmJournal
{
    public int Version { get; set; } = 1;
    public Dictionary<string, List<CalmOriginal>> Rows { get; set; } = [];
    public string ToJson() => JsonSerializer.Serialize(this);
    public static CalmJournal FromJson(string json)
    {
        var value = JsonSerializer.Deserialize<CalmJournal>(json) ?? throw new InvalidDataException("Empty undo journal.");
        if (value.Version != 1 || value.Rows is null || value.Rows.Any(p => p.Value is null || p.Value.Count == 0 || p.Value.Any(v => v.Address is null || v.Address.Machine)))
            throw new InvalidDataException("Unsupported undo journal.");
        return value;
    }
}
public interface ICalmJournalStore
{
    CalmJournal Load();
    void Save(CalmJournal journal);
}
/// <summary>Serialized write-ahead undo. Persistence failure never permits an unjournaled registry write.</summary>
public sealed class CalmChanges(IRegistryAccess registry, ICalmJournalStore store)
{
    private readonly object gate = new();
    private CalmJournal? journal;
    private CalmJournal Journal => journal ??= store.Load(); // A corrupt journal blocks changes; never silently discard it.
    public bool HasUndo(string id) { lock (gate) return Journal.Rows.ContainsKey(id); }
    public bool HasAnyUndo { get { lock (gate) return Journal.Rows.Count > 0; } }

    public void Apply(CalmSetting setting, bool calm, int? searchChoice = null)
    {
        lock (gate)
        {
            if (CalmEvaluator.IsManaged(setting, registry)) throw new InvalidOperationException("Set by a policy on this PC.");
            if (!calm && searchChoice is null && Journal.Rows.ContainsKey(setting.Id)) { UndoLocked(setting.Id); return; }
            if (setting.SearchChoice && searchChoice is not null && searchChoice is not (0 or 1 or 2)) throw new ArgumentOutOfRangeException(nameof(searchChoice));
            Change(setting.Id, setting.Values.Select(v => (v.Address, RegistryValue.Dword(setting.SearchChoice && searchChoice.HasValue ? searchChoice.Value : calm ? v.Calm : v.On))).ToArray());
        }
    }
    public void SetStartup(StartupEntry entry, bool enabled)
    {
        lock (gate)
        {
            if (enabled && Journal.Rows.ContainsKey(entry.Id)) { UndoLocked(entry.Id); return; }
            Change(entry.Id, [(entry.Approval, StartupApprovedCodec.Encode(enabled, registry.Read(entry.Approval)))]);
        }
    }
    public void CalmAll(IEnumerable<CalmReading> readings)
    {
        // Startup apps are deliberately not accepted by this API.
        foreach (var row in readings.Where(r => r.IsFinding)) Apply(row.Setting, true);
    }
    private void Change(string id, (RegistryAddress Address, RegistryValue Value)[] writes)
    {
        if (!Journal.Rows.ContainsKey(id))
        {
            var next = CalmJournal.FromJson(Journal.ToJson());
            next.Rows.Add(id, writes.Select(w => new CalmOriginal(w.Address, registry.Read(w.Address), !registry.KeyExists(w.Address.Path))).ToList());
            store.Save(next); journal = next;
        }
        // If any write fails, leave ALL originals durable, including the partially applied row.
        foreach (var w in writes) registry.Write(w.Address, w.Value);
    }
    public void Undo(string id) { lock (gate) UndoLocked(id); }
    public void UndoAll()
    {
        lock (gate)
        {
            // Validate the complete journal before restoring anything. One newly managed row should
            // not prevent unrelated, still-user-owned settings from being restored.
            foreach (var pair in Journal.Rows) ValidateTargets(pair.Key, pair.Value);
            var errors = new List<Exception>();
            foreach (string id in Journal.Rows.Keys.ToArray())
                try { UndoLocked(id); } catch (Exception ex) { errors.Add(ex); }
            if (errors.Count > 0) throw new AggregateException("Some changes could not be undone; their originals were retained.", errors);
        }
    }
    private void UndoLocked(string id)
    {
        if (!Journal.Rows.TryGetValue(id, out var originals)) return;
        ValidateTargets(id, originals);
        var setting = CalmCatalog.All.FirstOrDefault(s => s.Id == id);
        if (setting is not null && CalmEvaluator.IsManaged(setting, registry))
            throw new InvalidOperationException("This setting is now managed. Its undo record has been retained.");
        foreach (var original in originals)
            if (original.Previous is null) registry.Delete(original.Address);
            else registry.Write(original.Address, original.Previous);
        var next = CalmJournal.FromJson(Journal.ToJson());
        next.Rows.Remove(id);
        foreach (string path in originals.Where(o => o.KeyWasMissing).Select(o => o.Address.Path).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            // Several rows can share a new key. Transfer cleanup ownership until the final row is
            // undone, so Undo-all order does not strand an empty key. Never delete foreign content.
            var remaining = next.Rows.Values.FirstOrDefault(row => row.Any(o => o.Address.Path.Equals(path, StringComparison.OrdinalIgnoreCase)));
            if (remaining is not null)
            {
                int index = remaining.FindIndex(o => o.Address.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
                remaining[index] = remaining[index] with { KeyWasMissing = true };
            }
            else registry.DeleteKeyIfEmpty(path);
        }
        store.Save(next); journal = next;
    }
    private static void ValidateTargets(string id, List<CalmOriginal> originals)
    {
        var setting = CalmCatalog.All.FirstOrDefault(s => s.Id == id);
        if (setting is not null && originals.Count == setting.Values.Count && originals.Select(o => o.Address).Distinct().Count() == originals.Count && originals.All(o => setting.Values.Any(v => v.Address == o.Address))) return;
        string? prefix = id.StartsWith("startup-run:", StringComparison.Ordinal) ? "startup-run:" : id.StartsWith("startup-folder:", StringComparison.Ordinal) ? "startup-folder:" : null;
        if (prefix is not null && originals.Count == 1 && originals[0].Address == new RegistryAddress(StartupCatalog.Approved + (prefix == "startup-run:" ? "Run" : "StartupFolder"), id[prefix.Length..])) return;
        throw new InvalidDataException("Undo journal contains an unrecognized target.");
    }
}
