namespace FlowDesk.Core.Palette;

public enum CommandKind { Workspace, Action, App, Settings, File }
public sealed record MatchRange(int Start, int Length);
public sealed record CommandItem(string Id, string Title, string Subtitle, CommandKind Kind,
    string Icon, Func<Task> Primary, Func<Task>? Secondary = null, string Synonyms = "", string ActionHint = "Open");
public sealed record CommandMatch(CommandItem Item, double Score, IReadOnlyList<MatchRange> Highlights);

/// <summary>Providers query immutable snapshots; no source may touch dispatcher-owned state here.</summary>
public interface ICommandSource
{
    string Name { get; }
    IEnumerable<CommandMatch> Query(string query);
}

public sealed class SnapshotCommandSource(string name, Func<IReadOnlyList<CommandItem>> snapshot) : ICommandSource
{
    public string Name => name;
    public IEnumerable<CommandMatch> Query(string query)
    {
        foreach (var item in snapshot())
        {
            bool allowFuzzy = item.Kind != CommandKind.File;
            var match = CommandMatcher.Match(item.Title, query, allowFuzzy);
            var alias = CommandMatcher.Match(item.Synonyms, query, allowFuzzy);
            if (match.Score > 0 || alias.Score > 0 || string.IsNullOrWhiteSpace(query))
                yield return new(item, Math.Max(match.Score, alias.Score * .85), match.Highlights);
        }
    }
}

public sealed class CommandSearch(IEnumerable<ICommandSource> sources, PaletteUsageStore usage, Action<string, Exception> log)
{
    public IReadOnlyList<CommandMatch> Query(string query, CancellationToken token = default)
    {
        var matches = new List<CommandMatch>();
        foreach (var source in sources)
        {
            // Materialize inside the boundary: deferred iterators can throw after Query returns.
            try
            {
                token.ThrowIfCancellationRequested();
                var complete = new List<CommandMatch>();
                foreach (var match in source.Query(query)) { token.ThrowIfCancellationRequested(); complete.Add(match); }
                matches.AddRange(complete);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception ex) { log(source.Name, ex); }
        }
        bool recent = string.IsNullOrWhiteSpace(query);
        return matches.DistinctBy(m => m.Item.Id)
            .Where(m => !recent || usage.Get(m.Item.Id).Count > 0)
            .Select(m => m with { Score = recent ? usage.Get(m.Item.Id).LastUsed.UtcTicks : m.Score + usage.Boost(m.Item.Id) })
            .OrderByDescending(m => m.Score).ThenBy(m => Priority(m.Item.Kind))
            .ThenBy(m => m.Item.Title, StringComparer.OrdinalIgnoreCase).Take(80).ToArray();
    }
    private static int Priority(CommandKind kind) => kind switch { CommandKind.Workspace or CommandKind.Action => 0, CommandKind.App => 1, CommandKind.Settings => 2, _ => 3 };
}

/// <summary>Atomic snapshot publication; failed refreshes keep the last usable index.</summary>
public sealed class CommandIndex
{
    public event Action? Changed;
    private CommandItem[] items = [];
    private int refreshing;
    public IReadOnlyList<CommandItem> Snapshot => Volatile.Read(ref items);
    public async Task RefreshAsync(Func<Task<IReadOnlyList<CommandItem>>> load)
    {
        if (Interlocked.Exchange(ref refreshing, 1) != 0) return;
        try
        {
            var next = await load().ConfigureAwait(false);
            Volatile.Write(ref items, next.DistinctBy(i => i.Id).ToArray());
            Changed?.Invoke();
        }
        finally { Volatile.Write(ref refreshing, 0); }
    }
}
