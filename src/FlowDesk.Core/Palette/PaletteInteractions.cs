namespace FlowDesk.Core.Palette;

public sealed record PalettePrefix(string Prefix, PaletteMode Mode, string Label, string Placeholder, string Description);

public static class PalettePrefixes
{
    public static IReadOnlyList<PalettePrefix> All { get; } = Array.AsReadOnly(new PalettePrefix[]
    {
        new("w ", PaletteMode.Windows, "Windows", "Search open windows…", "Switch to an open window"),
        new("=", PaletteMode.Calculator, "Calculator", "Enter an expression or conversion…", "Calculate, convert units or find a local time"),
        new("?", PaletteMode.Web, "Web", "Search or enter a URL…", "Search the web or open an address")
    });

    public static PalettePrefix? Match(string text) => All.FirstOrDefault(p => text.StartsWith(p.Prefix, StringComparison.OrdinalIgnoreCase));
    public static PaletteMode Backspace(PaletteMode mode, string text) => text.Length == 0 ? PaletteMode.Commands : mode;
    public static IReadOnlyList<CommandMatch> Help(Func<PaletteMode, bool> enabled, Func<PaletteMode, Task> enter) => All
        .Where(p => enabled(p.Mode)).Select(p => new CommandMatch(new("prefix:" + p.Mode, p.Label + " · " + p.Prefix.TrimEnd(),
            p.Description, CommandKind.Help, "\uE897", () => enter(p.Mode), ActionHint: "Enter mode", KeepsPaletteOpen: true, RecordUsage: false), 1, [])).ToArray();
}

/// <summary>Restoring results is independent of which actions an item provides.</summary>
public sealed record PaletteActionPanel<T>(T Target, object? Results, int Selection, string Status);

/// <summary>Only a second deliberate Enter on the same action within the deadline may confirm.</summary>
public sealed class PaletteConfirmation
{
    public static readonly TimeSpan Duration = TimeSpan.FromSeconds(4);
    private (string Item, int Action, TimeSpan Started)? pending;
    public bool Enter(string item, int action, TimeSpan now)
    {
        if (pending is { } p && p.Item == item && p.Action == action && now >= p.Started && now - p.Started < Duration)
        { Cancel(); return true; }
        pending = (item, action, now);
        return false;
    }
    public void Cancel() => pending = null;
}
