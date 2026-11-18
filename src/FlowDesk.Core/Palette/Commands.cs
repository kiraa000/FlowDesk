namespace FlowDesk.Core.Palette;

public enum CommandKind { Workspace, Action, App, Settings, File, Clipboard, Window, Calculator, Web, System, Help, Run, Bookmark, Ai }
public sealed record PaletteAction(string Title, Func<Task> Run, string Glyph = "\uE76C",
    string? Shortcut = null, bool KeepsPaletteOpen = false, bool NeedsConfirm = false,
    string? ConfirmationPrompt = null, bool RequiresEnter = false);
public sealed record MatchRange(int Start, int Length);
public sealed record CommandItem(string Id, string Title, string Subtitle, CommandKind Kind,
    string Icon, Func<Task> Primary, Func<Task>? Secondary = null, string Synonyms = "", string ActionHint = "Open",
    bool KeepsPaletteOpen = false, bool RecordUsage = true, IReadOnlyList<PaletteAction>? NamedActions = null, bool IsFileSearch = false)
{
    public IReadOnlyList<PaletteAction> Actions => NamedActions ?? (Secondary is null
        ? [new(ActionHint, Primary, Shortcut: "Enter", KeepsPaletteOpen: KeepsPaletteOpen, RequiresEnter: Kind == CommandKind.Ai)]
        : [new(Kind == CommandKind.Workspace ? "Open" : ActionHint, Primary, Shortcut: "Enter", KeepsPaletteOpen: KeepsPaletteOpen),
           new(Kind switch { CommandKind.Workspace => "Edit in FlowDesk", CommandKind.App => "Run as administrator",
               CommandKind.File => "Show in Explorer", CommandKind.Clipboard => "Copy", _ => "More" },
               Secondary, Shortcut: "Ctrl+Enter", KeepsPaletteOpen: KeepsPaletteOpen)]);
    public string KeyboardHint => Actions.Count switch
    {
        0 => "No actions available",
        1 => "Enter · " + Actions[0].Title,
        _ => "Enter · " + Actions[0].Title + "   Ctrl+Enter · " + Actions[1].Title
    };
}
public sealed record CommandMatch(CommandItem Item, double Score, IReadOnlyList<MatchRange> Highlights);

/// <summary>Providers query immutable snapshots; no source may touch dispatcher-owned state here.</summary>
public interface ICommandSource
{
    string Name { get; }
    PaletteMode Mode => PaletteMode.Commands;
    IEnumerable<CommandMatch> Query(string query);
}

public sealed class SnapshotCommandSource(string name, Func<IReadOnlyList<CommandItem>> snapshot, PaletteMode mode = PaletteMode.Commands) : ICommandSource
{
    public string Name => name;
    public PaletteMode Mode => mode;
    public IEnumerable<CommandMatch> Query(string query)
    {
        string trimmed = query.Trim();
        foreach (var item in snapshot())
        {
            if (trimmed.Length == 0) { yield return new(item, 0, []); continue; }
            bool allowFuzzy = item.Kind != CommandKind.File;
            var match = CommandMatcher.Match(item.Title, trimmed, allowFuzzy);
            double alias = item.Synonyms.Length == 0 ? 0 : CommandMatcher.Match(item.Synonyms, trimmed, allowFuzzy).Score;
            if (match.Score > 0 || alias > 0)
                yield return new(item, item.Kind == CommandKind.Window &&
                    (item.Title.Equals(trimmed, StringComparison.OrdinalIgnoreCase) || item.Synonyms.Equals(trimmed, StringComparison.OrdinalIgnoreCase))
                    ? 10_000 : Math.Max(match.Score, alias * .85), match.Highlights);
        }
    }
}

public sealed class CommandSearch(IEnumerable<ICommandSource> sources, PaletteUsageStore usage, Action<string, Exception> log)
{
    public IReadOnlyList<CommandMatch> Query(string query, CancellationToken token = default, PaletteMode mode = PaletteMode.Commands)
    {
        var matches = new List<CommandMatch>();
        foreach (var source in sources)
        {
            if (mode != PaletteMode.Commands && mode != source.Mode) continue;
            // Materialize inside the boundary: deferred iterators can throw after Query returns.
            try
            {
                token.ThrowIfCancellationRequested();
                var complete = new List<CommandMatch>();
                foreach (var match in source is IModeCommandSource contextual ? contextual.Query(query, mode) : source.Query(query))
                { token.ThrowIfCancellationRequested(); complete.Add(match); }
                matches.AddRange(complete);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception ex) { log(source.Name, ex); }
        }
        bool recent = mode == PaletteMode.Commands && string.IsNullOrWhiteSpace(query);
        var ranked = matches.DistinctBy(m => m.Item.Id)
            .Where(m => !recent || (m.Item.RecordUsage && usage.Get(m.Item.Id).Count > 0))
            .Select(m => m with { Score = recent ? usage.Get(m.Item.Id).LastUsed.UtcTicks : m.Score + (m.Item.RecordUsage ? usage.Boost(m.Item.Id) : 0) })
            .OrderBy(m => PaletteResultMerge.Group(m, mode)).ThenByDescending(m => m.Score).ThenBy(m => Priority(m.Item.Kind))
            .ThenBy(m => m.Item.Title, StringComparer.OrdinalIgnoreCase).ToArray();
        return ranked.Length > 80 && (ranked[^1].Score == WebSearch.FallbackScore || ranked[^1].Item.Kind == CommandKind.Ai)
            ? [.. ranked.Take(79), ranked[^1]] : ranked.Take(80).ToArray();
    }
    private static int Priority(CommandKind kind) => kind switch { CommandKind.Workspace or CommandKind.Action => 0, CommandKind.App => 1, CommandKind.Settings => 2, _ => 3 };
}

public interface IModeCommandSource : ICommandSource
{
    IEnumerable<CommandMatch> Query(string query, PaletteMode mode);
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
