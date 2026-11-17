using System.Collections.Frozen;

namespace FlowDesk.Core.Hotkeys;

public enum ShortcutRegistrationResult { Active, Conflict, Unavailable }
public interface IShortcutRegistrar
{
    ShortcutRegistrationResult Register(int id, HotkeyBinding binding);
    bool Unregister(int id);
}

/// <summary>Message-thread owned. Unchanged bindings keep their registration; re-enabled bindings
/// get fresh Win32 IDs so a queued WM_HOTKEY from an old registration cannot execute after off/on.</summary>
public sealed class ShortcutRegistrations
{
    private readonly Dictionary<int, HotkeyBinding> registered = [];
    private int nextId = 1;
    public IReadOnlyList<int> NativeIds => registered.Keys.ToArray();
    public IReadOnlySet<string> RegisteredIds => registered.Values.Select(b => b.Id).ToFrozenSet(StringComparer.Ordinal);
    public bool TryGet(int id, out HotkeyBinding binding) => registered.TryGetValue(id, out binding!);
    public ShortcutRegistrationSnapshot Apply(ShortcutPolicy policy, bool desktopsAvailable, IShortcutRegistrar registrar,
        IReadOnlyList<HotkeyBinding>? bindings = null, bool atomic = false, IReadOnlySet<string>? requiredIds = null)
    {
        foreach (int id in retired.ToArray()) if (registrar.Unregister(id)) retired.Remove(id);
        var desired = (bindings ?? HotkeyCatalog.All).Where(b => policy.CanRegister(b, desktopsAvailable)).ToArray();
        var staged = new Dictionary<int, HotkeyBinding>();
        var acquired = new List<int>();
        var conflicts = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            foreach (var binding in desired)
            {
                if (staged.Values.Any(b => KeyChord.From(b) == KeyChord.From(binding)))
                {
                    if (atomic && (requiredIds is null || requiredIds.Contains(binding.Id))) throw new InvalidOperationException("Two enabled FlowDesk shortcuts use the same keys.");
                    conflicts.Add(binding.Id); continue;
                }
                // Transfer ownership of an already-held chord for swaps/turn-other-off. No unregister
                // gap, and no attempt to probe/register a chord owned by our own host.
                var held = registered.FirstOrDefault(p => KeyChord.From(p.Value) == KeyChord.From(binding));
                if (held.Key != 0) { staged.Add(held.Key, binding); continue; }
                if (nextId > 0xBFFF) throw new InvalidOperationException("Shortcut registration IDs exhausted. Restart FlowDesk.");
                int id = nextId++;
                var result = registrar.Register(id, binding);
                if (result == ShortcutRegistrationResult.Active) { acquired.Add(id); staged.Add(id, binding); }
                else
                {
                    if (atomic && (requiredIds is null || requiredIds.Contains(binding.Id))) throw new InvalidOperationException(result == ShortcutRegistrationResult.Conflict
                        ? "In use by another app. Your previous shortcut was kept." : "Windows could not register this shortcut. Your previous shortcut was kept.");
                    if (result == ShortcutRegistrationResult.Conflict) conflicts.Add(binding.Id);
                }
            }
        }
        catch
        {
            foreach (int id in acquired) if (!registrar.Unregister(id)) retired.Add(id);
            throw;
        }
        // All new keys are secured before any old key is released. If releasing fails, roll back
        // newly acquired keys and reacquire any already released old keys before reporting failure.
        var released = new List<int>();
        foreach (int id in registered.Keys.Where(id => !staged.ContainsKey(id)).ToArray())
        {
            if (!registrar.Unregister(id))
            {
                foreach (int added in acquired) if (!registrar.Unregister(added)) retired.Add(added);
                bool restored = true;
                foreach (int old in released)
                {
                    if (registrar.Register(old, registered[old]) == ShortcutRegistrationResult.Active) continue;
                    registered.Remove(old); restored = false;
                }
                if (!restored) throw new InvalidOperationException("Some previous shortcuts could not be restored. Turn window shortcuts off and on to retry.");
                throw new InvalidOperationException("A shortcut could not be unregistered. Your previous choices were kept.");
            }
            released.Add(id);
        }
        registered.Clear();
        foreach (var pair in staged) registered.Add(pair.Key, pair.Value);
        return new(registered.Values.Select(b => b.Id).ToFrozenSet(StringComparer.Ordinal), conflicts.ToFrozenSet(StringComparer.Ordinal));
    }
    private readonly HashSet<int> retired = [];
    public void Clear(IShortcutRegistrar registrar)
    {
        foreach (int id in registered.Keys) registrar.Unregister(id);
        foreach (int id in retired) registrar.Unregister(id);
        retired.Clear();
        registered.Clear();
    }
}
