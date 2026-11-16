using FlowDesk.Core.Palette;

namespace FlowDesk.Core.Tests;

public sealed class PaletteFocusCommandsTests
{
    [Fact] public async Task UnconfiguredFocusRemainsDiscoverableAndOpensSetupWithoutStarting()
    {
        int started = 0, setup = 0, stopped = 0;
        var items = PaletteFocusCommands.Create(new(false, false, false, false, false),
            _ => { started++; return Task.CompletedTask; }, () => { setup++; return Task.CompletedTask; }, () => { stopped++; return Task.CompletedTask; });
        var item = Assert.Single(items);
        Assert.Equal("Start Focus…", item.Title); Assert.Equal("Set up", item.ActionHint);
        Assert.Contains("Set up blocked apps", item.Subtitle);
        Assert.Single(new ActionCommandSource(() => items).Query("focus"));
        await item.Primary();
        Assert.Equal(1, setup); Assert.Equal(0, started); Assert.Equal(0, stopped);
    }
    [Fact] public async Task ConfiguredFocusHasThreeDurationsAndNoSetupAction()
    {
        var durations = new List<int>();
        var items = PaletteFocusCommands.Create(new(false, false, true, false, false),
            duration => { durations.Add(duration); return Task.CompletedTask; }, () => throw new Exception(), () => throw new Exception());
        Assert.Equal(new[] { "Start Focus 25 min", "Start Focus 50 min", "Start Focus 90 min" }, items.Select(i => i.Title));
        foreach (var item in items) await item.Primary();
        Assert.Equal(new[] { 25, 50, 90 }, durations);
    }
    [Fact] public async Task RunningSessionShowsOnlyStopEvenIfCanStartSnapshotIsStale()
    {
        bool stopped = false;
        var items = PaletteFocusCommands.Create(new(false, true, true, false, false),
            _ => throw new Exception(), () => throw new Exception(), () => { stopped = true; return Task.CompletedTask; });
        var item = Assert.Single(items); Assert.Equal("Stop Focus", item.Title);
        await item.Primary(); Assert.True(stopped);
    }
    [Fact] public void BusyFlowDeskDoesNotOfferFocusCommands() => Assert.Empty(PaletteFocusCommands.Create(
        new(true, false, false, false, false), _ => Task.CompletedTask, () => Task.CompletedTask, () => Task.CompletedTask));
}
