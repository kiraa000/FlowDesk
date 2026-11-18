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

public enum PaletteInvocation { Enter, Pointer, ModifiedEnter }
public enum PaletteExecution { Invalid, Prompted, Executed }

/// <summary>Single UI execution boundary: neither direct results nor action-panel rows bypass confirmation.</summary>
public sealed class PaletteActionExecutor(Func<TimeSpan> clock)
{
    private (string Item, int Index, PaletteAction Action, PaletteInvocation Input, TimeSpan Started)? pending;
    public bool IsPending => pending is not null;
    public void Cancel() => pending = null;

    public async Task<PaletteExecution> InvokeAsync(CommandItem item, int index, PaletteInvocation input,
        Action<PaletteAction> showPrompt, Action<PaletteAction> beforeRun)
    {
        var actions = item.Actions;
        if (index < 0 || index >= actions.Count) { Cancel(); return PaletteExecution.Invalid; }
        var action = actions[index];
        if (action.NeedsConfirm)
        {
            var now = clock();
            bool confirmed = input != PaletteInvocation.ModifiedEnter && pending is { } p &&
                p.Item == item.Id && p.Index == index && p.Action == action && p.Input == input &&
                now >= p.Started && now - p.Started < PaletteConfirmation.Duration;
            if (!confirmed)
            {
                pending = (item.Id, index, action, input, now);
                try { showPrompt(action); }
                catch { Cancel(); throw; } // a failed prompt is never authority for the next activation
                return PaletteExecution.Prompted;
            }
        }
        Cancel(); // consume before calling user code, including reentrant code
        beforeRun(action);
        await action.Run();
        return PaletteExecution.Executed;
    }
}
