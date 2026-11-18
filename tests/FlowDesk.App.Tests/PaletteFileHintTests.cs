using FlowDesk.App.Services;
using FlowDesk.Core.Palette;

namespace FlowDesk.App.Tests;

public sealed class PaletteFileHintTests
{
    [Fact] public async Task InstalledEverythingHasMatchingStatusAndStartsOnlyWhenInvoked()
    {
        int started = 0, advanced = 0;
        var installed = new InstalledFileBackend(() => { started++; return Task.CompletedTask; });
        var source = new PaletteFileSource(new("not-written.json"), _ => Task.CompletedTask, _ => { },
            () => { advanced++; return Task.CompletedTask; }, () => 0, _ => Task.CompletedTask,
            () => Task.FromResult<IPaletteFileBackend?>(installed));
        var status = await source.BeginOpen(true);
        Assert.Equal("Everything is installed but not running", status!.Name);
        Assert.Empty(await source.QueryAsync("report", PaletteMode.Commands, CancellationToken.None));
        var row = Assert.Single(await source.QueryAsync("report", PaletteMode.Files, CancellationToken.None));
        Assert.Equal(status.Name, row.Item.Title); Assert.Equal("Start Everything", row.Item.Subtitle);
        Assert.Equal("Start Everything", row.Item.Actions[0].Title);
        Assert.Equal(0, started); Assert.Equal(0, advanced);
        await row.Item.Primary(); Assert.Equal(1, started); Assert.Equal(0, advanced);
        await source.BeginOpen(false);
        Assert.Empty(await source.QueryAsync("report", PaletteMode.Files, CancellationToken.None));
    }
    [Fact] public async Task MissingBackendIsQuietInMixedModeAndFilesHintOpensAdvancedOnlyOnInvocation()
    {
        int opened = 0;
        var source = new PaletteFileSource(new("not-written.json"), _ => Task.CompletedTask, _ => { },
            () => { opened++; return Task.CompletedTask; }, () => 0, _ => Task.CompletedTask,
            () => Task.FromResult<IPaletteFileBackend?>(null));
        await source.BeginOpen(true);
        Assert.Empty(await source.QueryAsync("any query", PaletteMode.Commands, CancellationToken.None));
        Assert.Empty(await source.QueryAsync("", PaletteMode.Commands, CancellationToken.None));
        var row = Assert.Single(await source.QueryAsync("report", PaletteMode.Files, CancellationToken.None));
        Assert.Equal("File search needs Everything or Windows Search", row.Item.Title); Assert.Equal(0, opened);
        await row.Item.Actions[0].Run(); Assert.Equal(1, opened);
    }
    [Fact] public async Task FailingBackendAlsoDoesNotAddAnErrorRowToEveryMixedQuery()
    {
        var source = new PaletteFileSource(new("not-written.json"), _ => Task.CompletedTask, _ => { },
            () => Task.CompletedTask, () => 0, _ => Task.CompletedTask,
            () => Task.FromResult<IPaletteFileBackend?>(new Broken()));
        await source.BeginOpen(true);
        Assert.Empty(await source.QueryAsync("report", PaletteMode.Commands, CancellationToken.None));
        Assert.Contains("unavailable", Assert.Single(await source.QueryAsync("report", PaletteMode.Files, CancellationToken.None)).Item.Title);
    }
    private sealed class Broken : IPaletteFileBackend
    { public string Name => "fake"; public Task<IReadOnlyList<PaletteFile>> SearchAsync(string query, int limit, CancellationToken token) => throw new IOException("Fake index unavailable"); }
}
