using FlowDesk.Core.Hotkeys;

namespace FlowDesk.App.Services;

/// <summary>Uses the existing transactional native rebinder; independent failures keep each binding's default.</summary>
public static class ImportedShortcuts
{
    public static async Task<IReadOnlyList<string>> ApplyAsync(AppSettings settings, Func<IReadOnlyDictionary<string, string>, Task> apply)
    {
        var requested = new Dictionary<string, string>(settings.ShortcutOverrides);
        try { await apply(requested); return []; }
        catch (Exception)
        {
            var warnings = new List<string>();
            var accepted = new Dictionary<string, string>();
            settings.ShortcutOverrides = accepted;
            await apply(accepted);
            foreach (var pair in requested)
            {
                var candidate = new Dictionary<string, string>(accepted) { [pair.Key] = pair.Value };
                try
                {
                    var table = ShortcutRebinding.Effective(candidate).Where(b => !b.IsMouseWheel && !settings.DisabledShortcuts.Contains(b.Id));
                    if (table.GroupBy(KeyChord.From).Any(g => g.Count() > 1)) throw new InvalidOperationException();
                    await apply(candidate);
                    accepted = candidate;
                }
                catch (Exception) { warnings.Add($"Shortcut '{pair.Key}' could not be used; the default was kept."); }
            }
            settings.ShortcutOverrides = accepted;
            return warnings;
        }
    }
}
