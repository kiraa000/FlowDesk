using System.Runtime.CompilerServices;

namespace FlowDesk.Core.Palette;

public interface IAsyncCommandSource
{
    string Name { get; }
    bool Supports(PaletteMode mode);
    Task<IReadOnlyList<CommandMatch>> QueryAsync(string query, PaletteMode mode, CancellationToken token);
}

/// <summary>Each enumeration is one query lease. Fast sources publish first; slow sources cannot
/// publish after cancellation even if their native backend ignores it. The UI also guards its open epoch.</summary>
public sealed class PaletteStreamingSearch(CommandSearch synchronous, IEnumerable<IAsyncCommandSource> asynchronous,
    Action<string, Exception> log, Func<CancellationToken, Task>? debounce = null)
{
    public async IAsyncEnumerable<IReadOnlyList<CommandMatch>> QueryAsync(string query, PaletteMode mode,
        [EnumeratorCancellation] CancellationToken token = default)
    {
        using var lease = CancellationTokenSource.CreateLinkedTokenSource(token);
        token = lease.Token;
        try
        {
        var initial = await Task.Run(() => synchronous.Query(query, token, mode), token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        yield return PaletteResultMerge.Merge(initial, mode);
        var batches = new List<IReadOnlyList<CommandMatch>> { initial };
        var pending = asynchronous.Where(s => s.Supports(mode)).Select(ReadAsync).ToList();
        while (pending.Count > 0)
        {
            var completed = await Task.WhenAny(pending).WaitAsync(token).ConfigureAwait(false);
            pending.Remove(completed);
            var batch = await completed.ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            batches.Add(batch);
            yield return PaletteResultMerge.Merge(batches.SelectMany(b => b), mode);
        }

        async Task<IReadOnlyList<CommandMatch>> ReadAsync(IAsyncCommandSource source)
        {
            try
            {
                await (debounce?.Invoke(token) ?? Task.Delay(80, token)).ConfigureAwait(false);
                var result = await source.QueryAsync(query, mode, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                return result;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return []; }
            catch (Exception ex) { log(source.Name, ex); return []; }
        }
        }
        finally { lease.Cancel(); }
    }
}

public static class PaletteResultMerge
{
    public static int Group(CommandMatch match, PaletteMode mode) => mode != PaletteMode.Commands ? 0 :
        match.Score == WebSearch.FallbackScore ? 3 : match.Item.IsFileSearch ? 2 : match.Item.Kind == CommandKind.Bookmark ? 1 : 0;
    public static IReadOnlyList<CommandMatch> Merge(IEnumerable<CommandMatch> matches, PaletteMode mode)
    {
        var ranked = matches.DistinctBy(m => m.Item.Id)
            .OrderBy(m => Group(m, mode))
            .ThenByDescending(m => m.Score)
            .ThenBy(m => m.Item.Kind switch { CommandKind.Workspace or CommandKind.Action => 0, CommandKind.App => 1, CommandKind.Settings => 2, _ => 3 })
            .ThenBy(m => m.Item.Title, StringComparer.OrdinalIgnoreCase);
        int files = 0;
        return ranked.Where(m => mode != PaletteMode.Commands || !m.Item.IsFileSearch || ++files <= 5).Take(80).ToArray();
    }
    public static int Selection(IReadOnlyList<CommandMatch> results, string? selectedId)
    {
        if (results.Count == 0) return -1;
        for (int i = 0; i < results.Count; i++) if (results[i].Item.Id == selectedId) return i;
        return 0;
    }
}
