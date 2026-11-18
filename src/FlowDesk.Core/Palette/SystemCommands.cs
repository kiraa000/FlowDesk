namespace FlowDesk.Core.Palette;

public enum SystemCommand { Lock, Sleep, Hibernate, SignOut, Restart, ShutDown, EmptyRecycleBin, Dark, Light,
    Mute, Unmute, DndOn, DndOff, ShowDesktop, TaskManager, Snipping }
public sealed record SystemPaletteState(bool Hibernate, long? BinItems, long BinBytes, bool IsLight, bool? IsMuted, bool? IsDnd);
public interface IPaletteSystem
{
    Task<SystemPaletteState> ReadAsync();
    Task RunAsync(SystemCommand command);
}

public static class SystemCommands
{
    public static IReadOnlyList<CommandItem> Create(IPaletteSystem system, SystemPaletteState state)
    {
        List<CommandItem> rows = [];
        void Add(SystemCommand command, string title, string synonyms, string glyph, string subtitle = "System command",
            string? confirm = null, bool enabled = true)
        {
            Task Run() => enabled ? system.RunAsync(command) : Task.CompletedTask;
            rows.Add(new("system:" + command, title, subtitle, CommandKind.System, glyph, Run, Synonyms: synonyms,
                RecordUsage: confirm is null && enabled, NamedActions: enabled
                    ? [new(title, Run, glyph, "Enter", NeedsConfirm: confirm is not null, ConfirmationPrompt: confirm)] : []));
        }
        Add(SystemCommand.Lock, "Lock", "lock workstation screen", "\uE72E");
        Add(SystemCommand.Sleep, "Sleep", "suspend standby", "\uE708");
        if (state.Hibernate) Add(SystemCommand.Hibernate, "Hibernate", "suspend to disk", "\uE7E8");
        Add(SystemCommand.SignOut, "Sign out", "log off logout", "\uE8AC", confirm: "Press Enter again to sign out");
        Add(SystemCommand.Restart, "Restart", "reboot", "\uE777", confirm: "Press Enter again to restart");
        Add(SystemCommand.ShutDown, "Shut down", "shutdown power off", "\uE7E8", confirm: "Press Enter again to shut down");
        if (state.BinItems is long items)
        {
            string[] units = ["B", "KiB", "MiB", "GiB", "TiB"];
            double size = state.BinBytes;
            int unit = 0;
            while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
            Add(SystemCommand.EmptyRecycleBin, "Empty Recycle Bin", "trash empty delete", "\uE74D",
                items == 0 ? "Recycle Bin is empty" : $"{items:N0} {(items == 1 ? "item" : "items")} · {size:0.#} {units[unit]}",
                "Press Enter again to empty the Recycle Bin", items > 0);
        }
        Add(state.IsLight ? SystemCommand.Dark : SystemCommand.Light, state.IsLight ? "Switch to dark mode" : "Switch to light mode", "theme appearance", "\uE793");
        if (state.IsMuted is bool muted) Add(muted ? SystemCommand.Unmute : SystemCommand.Mute, muted ? "Unmute sound" : "Mute sound", "volume audio", "\uE767");
        if (state.IsDnd is bool dnd) Add(dnd ? SystemCommand.DndOff : SystemCommand.DndOn, dnd ? "Do not disturb off" : "Do not disturb on", "dnd notifications quiet", "\uE7ED");
        Add(SystemCommand.ShowDesktop, "Show desktop", "hide all windows", "\uE7F4");
        Add(SystemCommand.TaskManager, "Task Manager", "processes taskmgr", "\uE9D9");
        Add(SystemCommand.Snipping, "Snipping", "screenshot snipping tool screen capture", "\uE8C6");
        return rows;
    }
}
