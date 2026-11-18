namespace FlowDesk.Core.Palette;

public enum CustomTargetKind { Url, Folder, File, Program, PowerShellScript }
public sealed record CustomCommandDefinition(string Id, string Name, string Keywords, CustomTargetKind Kind,
    string Target, string Arguments = "", bool RunAsAdmin = false, string Icon = "");
public sealed class CustomCommandSettings
{
    public int Version { get; set; } = 1;
    public List<CustomCommandDefinition> Entries { get; set; } = [];
}
public sealed record CustomCommandLaunch(string Target, string Arguments, bool RunAsAdmin);

public static class CustomCommandRules
{
    private static readonly string[] Reserved = ["w", "f", "b", "ai", "=", "?", ">", "help"];
    public static string[] Keywords(string text) => text.Split([' ', ',', ';', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    public static string? Validate(CustomCommandDefinition command, IEnumerable<CustomCommandDefinition> others, Func<string, bool>? exists = null)
    {
        if (string.IsNullOrWhiteSpace(command.Name)) return "Enter a command name.";
        if (string.IsNullOrWhiteSpace(command.Id)) return "This command has no identity. Add it again.";
        if (!Enum.IsDefined(command.Kind)) return "Choose a target kind.";
        var words = Keywords(command.Keywords ?? "");
        if (words.Length == 0) return "Enter at least one keyword.";
        if (words.Any(w => Reserved.Contains(w, StringComparer.OrdinalIgnoreCase) || !w.All(c => char.IsLetterOrDigit(c) || c is '-' or '_')))
            return "Use letters, numbers, hyphens or underscores, not a built-in prefix.";
        if (words.Distinct(StringComparer.OrdinalIgnoreCase).Count() != words.Length || others.Where(c => c.Id != command.Id)
            .SelectMany(c => Keywords(c.Keywords ?? "")).Any(w => words.Contains(w, StringComparer.OrdinalIgnoreCase))) return "That keyword is already in use.";
        string target = command.Target ?? "";
        if (command.Kind == CustomTargetKind.Url)
        {
            if (!Uri.TryCreate(target.Replace("{query}", "sample"), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
                return "Enter an HTTP or HTTPS URL.";
            if (command.RunAsAdmin) return "Only programs and scripts can run as administrator.";
        }
        else
        {
            if (!Path.IsPathFullyQualified(Environment.ExpandEnvironmentVariables(target)) || target.Contains("{query}", StringComparison.Ordinal))
                return "Enter a full file or folder path, without {query}.";
            if (command.Kind == CustomTargetKind.PowerShellScript && !Path.GetExtension(target).Equals(".ps1", StringComparison.OrdinalIgnoreCase))
                return "Choose a PowerShell .ps1 script file.";
            if (exists is not null && !exists(Environment.ExpandEnvironmentVariables(target))) return "That file or folder could not be found.";
            if (command.RunAsAdmin && command.Kind is not (CustomTargetKind.Program or CustomTargetKind.PowerShellScript)) return "Only programs and scripts can run as administrator.";
        }
        return null;
    }
    public static CustomCommandLaunch Build(CustomCommandDefinition command, string query)
    {
        string? error = Validate(command, []);
        if (error is not null) throw new ArgumentException(error);
        string target = command.Kind == CustomTargetKind.Url ? command.Target.Replace("{query}", Uri.EscapeDataString(query)) : Environment.ExpandEnvironmentVariables(command.Target);
        string arguments = (command.Arguments ?? "").Replace("{query}", query);
        return command.Kind == CustomTargetKind.PowerShellScript
            ? new("powershell.exe", "-NoProfile -File " + Quote(target) + (arguments.Length == 0 ? "" : " " + arguments), command.RunAsAdmin)
            : new(target, command.Kind is CustomTargetKind.Program ? arguments : "", command.RunAsAdmin);
    }
    public static string Quote(string value)
    {
        var text = new System.Text.StringBuilder("\""); int slashes = 0;
        foreach (char c in value)
        {
            if (c == '\\') { slashes++; continue; }
            text.Append('\\', c == '"' ? slashes * 2 + 1 : slashes); text.Append(c); slashes = 0;
        }
        return text.Append('\\', slashes * 2).Append('"').ToString();
    }
}

/// <summary>Only builds inert rows while typing. The supplied launcher is called solely by the action delegate.</summary>
public sealed class CustomCommandSource(Func<CustomCommandSettings> settings, Func<CustomCommandLaunch, Task> launch) : ICommandSource
{
    public string Name => "Custom commands";
    public IEnumerable<CommandMatch> Query(string query)
    {
        var current = settings();
        if (current.Version != 1) yield break;
        string trimmed = query.TrimStart(); int space = trimmed.IndexOf(' ');
        string keyword = space < 0 ? trimmed : trimmed[..space];
        foreach (var command in current.Entries ?? [])
        {
            if (CustomCommandRules.Validate(command, current.Entries ?? []) is not null) continue;
            bool keyed = CustomCommandRules.Keywords(command.Keywords).Contains(keyword, StringComparer.OrdinalIgnoreCase);
            var match = CommandMatcher.Match(command.Name, trimmed);
            if (!keyed && match.Score == 0) continue;
            string tail = keyed && space >= 0 ? trimmed[(space + 1)..] : "";
            var plan = CustomCommandRules.Build(command, tail);
            string icon = !string.IsNullOrEmpty(command.Icon) ? command.Icon : command.Kind is CustomTargetKind.Program or CustomTargetKind.File ? command.Target : "\uE756";
            yield return new(new("custom:" + command.Id, command.Name, command.Kind == CustomTargetKind.Url ? plan.Target : command.Target + (plan.Arguments.Length > 0 ? " · " + plan.Arguments : ""),
                CommandKind.Action, icon, () => launch(plan), Synonyms: command.Keywords, ActionHint: "Run"), keyed ? 1000 : match.Score, keyed ? [] : match.Highlights);
        }
    }
}
