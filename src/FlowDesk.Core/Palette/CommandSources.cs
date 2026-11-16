namespace FlowDesk.Core.Palette;

public sealed class WorkspaceCommandSource(Func<IReadOnlyList<CommandItem>> snapshot) : ICommandSource
{
    public string Name => "Workspaces";
    public IEnumerable<CommandMatch> Query(string query) => new SnapshotCommandSource(Name, snapshot).Query(query);
}
public sealed class ActionCommandSource(Func<IReadOnlyList<CommandItem>> applicable) : ICommandSource
{
    public string Name => "FlowDesk actions";
    public IEnumerable<CommandMatch> Query(string query) => new SnapshotCommandSource(Name, applicable).Query(query);
}
public sealed class AppCommandSource(CommandIndex index) : ICommandSource
{
    public string Name => "Apps";
    public IEnumerable<CommandMatch> Query(string query) => new SnapshotCommandSource(Name, () => index.Snapshot).Query(query);
}
public sealed class SettingsCommandSource(Func<string, Task> open) : ICommandSource
{
    public string Name => "Windows Settings";
    public IEnumerable<CommandMatch> Query(string query) => new SnapshotCommandSource(Name, () => SettingsPageCatalog.All.Select(p =>
        new CommandItem("settings:" + p.Id, p.Title, "Settings page", CommandKind.Settings, "\uE713", () => open(p.Uri), Synonyms: p.Synonyms)).ToArray()).Query(query);
}
public sealed class RecentFileCommandSource(CommandIndex index) : ICommandSource
{
    public string Name => "Recent files";
    public IEnumerable<CommandMatch> Query(string query) => new SnapshotCommandSource(Name, () => index.Snapshot).Query(query);
    public static IReadOnlyList<CommandItem> Resolve(IEnumerable<string> links, Func<string, string?> resolve,
        Func<string, bool> exists, Func<string, Task> open, Func<string, Task> location, Action<Exception> log)
    {
        var items = new List<CommandItem>();
        foreach (string link in links)
        {
            try
            {
                string? target = resolve(link);
                if (string.IsNullOrWhiteSpace(target) || !exists(target)) continue;
                items.Add(new("file:" + target.ToUpperInvariant(), Path.GetFileName(target.TrimEnd('\\')), "File · " + target,
                    CommandKind.File, target, () => open(target), () => location(target)));
            }
            catch (Exception ex) { log(ex); }
        }
        return items.DistinctBy(i => i.Id).ToArray();
    }
}
