namespace FlowDesk.Core.Palette;

/// <summary>Discoverable without changing Focus's safety rule: an empty block list needs setup,
/// not an implicit block-every-app session. Delegates run on the palette's UI command path.</summary>
public static class PaletteFocusCommands
{
    public static IReadOnlyList<CommandItem> Create(PaletteActionState state, Func<int, Task> start, Func<Task> setup, Func<Task> stop)
    {
        if (state.Busy) return [];
        if (state.FocusRunning)
            return [new("action:focus-stop", "Stop Focus", "FlowDesk action", CommandKind.Action, "\uE71A", stop, ActionHint: "Stop")];
        if (!state.CanStartFocus)
            return [new("action:focus-setup", "Start Focus…", "FlowDesk action · Set up blocked apps first", CommandKind.Action, "\uE916", setup,
                Synonyms: "start focus timer 25 50 90 setup", ActionHint: "Set up")];
        return new[] { 25, 50, 90 }.Select(duration => new CommandItem("action:focus-" + duration, $"Start Focus {duration} min",
            "FlowDesk action", CommandKind.Action, "\uE916", () => start(duration), ActionHint: "Start")).ToArray();
    }
}
