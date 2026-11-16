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
    public bool TryGet(int id, out HotkeyBinding binding) => registered.TryGetValue(id, out binding!);
    public ShortcutRegistrationSnapshot Apply(ShortcutPolicy policy, bool desktopsAvailable, IShortcutRegistrar registrar)
    {
        foreach (var entry in registered.ToArray())
        {
            if (policy.CanRegister(entry.Value, desktopsAvailable)) continue;
            if (!registrar.Unregister(entry.Key)) throw new InvalidOperationException("A shortcut could not be unregistered.");
            registered.Remove(entry.Key);
        }
        var conflicts = new HashSet<string>(StringComparer.Ordinal);
        foreach (var binding in HotkeyCatalog.Registerable)
        {
            if (!policy.CanRegister(binding, desktopsAvailable) || registered.Values.Any(b => b.Id == binding.Id)) continue;
            if (nextId > 0xBFFF) continue; // never recycle IDs while old native messages could still be queued
            int id = nextId++;
            var result = registrar.Register(id, binding);
            if (result == ShortcutRegistrationResult.Active) registered.Add(id, binding);
            else if (result == ShortcutRegistrationResult.Conflict) conflicts.Add(binding.Id);
        }
        return new(registered.Values.Select(b => b.Id).ToFrozenSet(StringComparer.Ordinal), conflicts.ToFrozenSet(StringComparer.Ordinal));
    }
    public void Clear(IShortcutRegistrar registrar)
    {
        foreach (int id in registered.Keys) registrar.Unregister(id);
        registered.Clear();
    }
}
