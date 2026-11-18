using FlowDesk.Core.Palette;

namespace FlowDesk.Core.Tests;

public sealed class PaletteStreamingTests
{
    private static CommandMatch Match(string id, CommandKind kind = CommandKind.App, double score = 10) =>
        new(new(id, id, "", kind, "", () => Task.CompletedTask, IsFileSearch: kind == CommandKind.File), score, []);
    private static CommandSearch Fast(params CommandMatch[] matches) => new([new FastSource(matches)],
        new PaletteUsageStore("not-written.json"), (_, _) => throw new Exception("Unexpected source error"));
    private sealed class FastSource(CommandMatch[] matches) : ICommandSource
    { public string Name => "fast"; public IEnumerable<CommandMatch> Query(string _) => matches; }
    private sealed class SlowSource(Func<CancellationToken, Task<IReadOnlyList<CommandMatch>>> query) : IAsyncCommandSource
    {
        public int Calls;
        public string Name => "slow";
        public bool Supports(PaletteMode mode) => mode is PaletteMode.Files or PaletteMode.Commands;
        public Task<IReadOnlyList<CommandMatch>> QueryAsync(string _, PaletteMode mode, CancellationToken token) { Calls++; return query(token); }
    }
    [Fact] public async Task SyncResultsArriveBeforeDebounceOrSlowSourceStarts()
    {
        var delay = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var slow = new SlowSource(_ => Task.FromResult<IReadOnlyList<CommandMatch>>([Match("file", CommandKind.File)]));
        var pipeline = new PaletteStreamingSearch(Fast(Match("app")), [slow], (_, _) => { }, _ => delay.Task);
        await using var results = pipeline.QueryAsync("query", PaletteMode.Commands).GetAsyncEnumerator();
        Assert.True(await results.MoveNextAsync()); Assert.Equal("app", Assert.Single(results.Current).Item.Id); Assert.Equal(0, slow.Calls);
        var next = results.MoveNextAsync().AsTask(); Assert.False(next.IsCompleted); Assert.Equal(0, slow.Calls);
        delay.SetResult(); Assert.True(await next); Assert.Equal(1, slow.Calls); Assert.Equal(2, results.Current.Count);
    }
    [Fact] public async Task CancellationDuringDebounceNeverCallsBackend()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var slow = new SlowSource(_ => throw new Exception("Must not run"));
        var pipeline = new PaletteStreamingSearch(Fast(), [slow], (_, _) => { }, token =>
        { entered.SetResult(); return Task.Delay(Timeout.Infinite, token); });
        using var cancel = new CancellationTokenSource();
        await using var results = pipeline.QueryAsync("old", PaletteMode.Files, cancel.Token).GetAsyncEnumerator();
        Assert.True(await results.MoveNextAsync());
        var next = results.MoveNextAsync().AsTask(); await entered.Task; cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => next); Assert.Equal(0, slow.Calls);
    }
    [Fact] public async Task CancelledUncooperativeBackendCannotPublishStaleRows()
    {
        var native = new TaskCompletionSource<IReadOnlyList<CommandMatch>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var slow = new SlowSource(_ => native.Task);
        var pipeline = new PaletteStreamingSearch(Fast(), [slow], (_, _) => { }, _ => Task.CompletedTask);
        using var cancel = new CancellationTokenSource();
        await using var results = pipeline.QueryAsync("old", PaletteMode.Files, cancel.Token).GetAsyncEnumerator();
        Assert.True(await results.MoveNextAsync()); var next = results.MoveNextAsync().AsTask();
        cancel.Cancel(); native.SetResult([Match("old-file", CommandKind.File)]);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => next);
    }
    [Fact] public async Task OneSlowSourceCannotBlockAnotherAndFailuresPreserveFastRows()
    {
        var blocked = new TaskCompletionSource<IReadOnlyList<CommandMatch>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var slow = new SlowSource(_ => blocked.Task);
        var broken = new SlowSource(_ => throw new IOException("Index unavailable"));
        int errors = 0;
        var pipeline = new PaletteStreamingSearch(Fast(Match("app")), [slow, broken], (_, _) => errors++, _ => Task.CompletedTask);
        await using var results = pipeline.QueryAsync("x", PaletteMode.Commands).GetAsyncEnumerator();
        Assert.True(await results.MoveNextAsync()); Assert.True(await results.MoveNextAsync());
        Assert.Equal("app", Assert.Single(results.Current).Item.Id); Assert.Equal(1, errors);
        blocked.SetResult([Match("file", CommandKind.File)]); Assert.True(await results.MoveNextAsync()); Assert.Equal(2, results.Current.Count);
    }
    [Fact] public void MixedFilesAreCappedBelowAppsWindowsAndWorkspacesAndSelectionUsesIdentity()
    {
        var files = Enumerable.Range(0, 12).Select(i => Match("file" + i, CommandKind.File, 10000));
        var app = Match("app"); var window = Match("window", CommandKind.Window); var workspace = Match("workspace", CommandKind.Workspace);
        var merged = PaletteResultMerge.Merge([app, window, workspace, .. files, app], PaletteMode.Commands);
        Assert.Equal(8, merged.Count); Assert.All(merged.Take(3), m => Assert.NotEqual(CommandKind.File, m.Item.Kind));
        Assert.Equal("window", merged[PaletteResultMerge.Selection(merged, "window")].Item.Id);
        Assert.Equal(0, PaletteResultMerge.Selection(merged, "gone")); Assert.Equal(-1, PaletteResultMerge.Selection([], "gone"));
        Assert.Equal(12, PaletteResultMerge.Merge(files, PaletteMode.Files).Count);
    }
    [Fact] public async Task FileSourceDoesNotRunInOtherModes()
    {
        var slow = new SlowSource(_ => throw new Exception());
        var pipeline = new PaletteStreamingSearch(Fast(), [slow], (_, _) => { });
        await foreach (var _ in pipeline.QueryAsync("x", PaletteMode.Calculator)) { }
        Assert.Equal(0, slow.Calls);
    }
    [Fact] public void RecentFileRowsKeepTheirUsageRankingAndAreNotSearchCapped()
    {
        var recent = Enumerable.Range(0, 8).Select(i => Match("recent" + i, CommandKind.File, 100) with
            { Item = Match("recent" + i, CommandKind.File).Item with { IsFileSearch = false } });
        var merged = PaletteResultMerge.Merge([Match("app"), .. recent], PaletteMode.Commands);
        Assert.Equal(9, merged.Count); Assert.StartsWith("recent", merged[0].Item.Id);
    }
    [Fact] public async Task DisposingEnumerationCancelsStillRunningProviders()
    {
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blocked = new SlowSource(async token =>
        {
            using var registration = token.Register(() => cancelled.TrySetResult());
            await Task.Delay(Timeout.Infinite, token); return [];
        });
        var completed = new SlowSource(_ => Task.FromResult<IReadOnlyList<CommandMatch>>([]));
        var pipeline = new PaletteStreamingSearch(Fast(), [blocked, completed], (_, _) => { }, _ => Task.CompletedTask);
        var results = pipeline.QueryAsync("x", PaletteMode.Files).GetAsyncEnumerator();
        Assert.True(await results.MoveNextAsync()); Assert.True(await results.MoveNextAsync());
        await results.DisposeAsync(); Assert.True(cancelled.Task.IsCompleted);
    }
}
