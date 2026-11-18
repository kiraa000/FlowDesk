namespace FlowDesk.Core.Palette;

public sealed record RunCommandLine(string Original, string Expanded, string Target, string Arguments)
{
    public CustomCommandLaunch Launch(bool elevated = false) => new(Target, Arguments, elevated);
    public CustomCommandLaunch Terminal() => new("cmd.exe", "/k \"" + Expanded + "\"", false);
}

public static class RunLineParser
{
    public static RunCommandLine? Parse(string line, Func<string, string>? expand = null)
    {
        string original = line.Trim();
        if (original.Length == 0 || original.Any(c => char.IsControl(c) && c != '\t')) return null;
        string expanded = (expand ?? Environment.ExpandEnvironmentVariables)(original).Trim();
        if (expanded.Length == 0 || expanded.Any(c => char.IsControl(c) && c != '\t')) return null;
        var target = new System.Text.StringBuilder();
        int at = 0; bool quoted = false;
        while (at < expanded.Length)
        {
            char c = expanded[at];
            if (!quoted && char.IsWhiteSpace(c)) break;
            if (c == '"') { quoted = !quoted; at++; continue; }
            target.Append(c); at++;
        }
        if (quoted || target.Length == 0) return null;
        string args = expanded[at..].TrimStart();
        return new(original, expanded, target.ToString(), args);
    }
}

public sealed class RunCommandSource(PaletteUsageStore usage, Func<CustomCommandLaunch, Task> launch,
    Func<Task> save, Action changed) : IModeCommandSource
{
    public string Name => "Run command";
    public PaletteMode Mode => PaletteMode.Run;
    public IEnumerable<CommandMatch> Query(string query) => Query(query, PaletteMode.Run);
    public IEnumerable<CommandMatch> Query(string query, PaletteMode mode)
    {
        if (mode != PaletteMode.Run) yield break;
        if (query.Trim().Length > 0)
        {
            if (RunLineParser.Parse(query) is { } parsed) yield return Row(parsed, fromHistory: false);
            else yield return new(new("run:invalid", "Enter a command with a balanced quoted path", "For example: notepad file.txt", CommandKind.Help,
                "\uE756", () => Task.CompletedTask, RecordUsage: false, NamedActions: []), 1, []);
        }
        else foreach (string line in usage.RunHistory()) if (RunLineParser.Parse(line) is { } parsed) yield return Row(parsed, fromHistory: true);
    }
    private CommandMatch Row(RunCommandLine command, bool fromHistory)
    {
        var actions = new List<PaletteAction>
        {
            new("Run", () => ExecuteAsync(command, command.Launch()), "\uE768", "Enter"),
            new("Run as administrator", () => ExecuteAsync(command, command.Launch(true)), "\uEA18", "Ctrl+Enter"),
            new("Run in terminal", () => ExecuteAsync(command, command.Terminal()), "\uE756")
        };
        if (fromHistory) actions.Add(new("Delete from history", async () =>
        { usage.DeleteRun(command.Original); await save(); changed(); }, "\uE74D", KeepsPaletteOpen: true));
        return new(new("run:" + command.Original, command.Original, command.Target + (command.Arguments.Length > 0 ? " · " + command.Arguments : ""),
            CommandKind.Run, "\uE756", actions[0].Run, RecordUsage: false, NamedActions: actions.AsReadOnly()), 1, []);
    }
    private async Task ExecuteAsync(RunCommandLine line, CustomCommandLaunch plan)
    {
        await launch(plan);
        usage.RecordRun(line.Original, DateTimeOffset.UtcNow); await save();
    }
}
