using FlowDesk.App.Autosave;

namespace FlowDesk.App.Tests;

public sealed class AutoSaveTextTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void InactiveWorkspaceStillShowsOnStateAndLastSavedAge()
    {
        string text = AutoSaveText.For(enabled: true, status: null, layoutSavedAt: Now.AddMinutes(-2), Now);
        Assert.Equal("Autosave on · saved 2 min ago", text);
    }

    [Fact]
    public void OffAndNeverSavedAreHonest()
    {
        Assert.Equal("Autosave off · saved 1 hour ago", AutoSaveText.For(false, null, Now.AddHours(-1), Now));
        Assert.Equal("Autosave on · not saved yet", AutoSaveText.For(true, null, null, Now));
    }

    [Fact]
    public void ToggleOnWinsOverAStaleOffStatus()
    {
        var staleOff = new AutoSaveStatus("ws", AutoSaveState.Off, Now.AddMinutes(-2));
        Assert.Equal("Autosave on · saved 2 min ago", AutoSaveText.For(true, staleOff, Now.AddMinutes(-2), Now));
    }

    [Fact]
    public void PendingAndFailedDoNotKeepShowingAnOldSavedState()
    {
        var oldSaved = new AutoSaveStatus("ws", AutoSaveState.Saved, Now.AddHours(-1));
        var pending = new AutoSaveStatus("ws", AutoSaveState.Pending, Now.AddHours(-1));
        var failed = new AutoSaveStatus("ws", AutoSaveState.Failed, Now.AddHours(-1));

        Assert.Equal("Autosave on · changes pending", AutoSaveText.For(true, pending, Now.AddHours(-1), Now));
        Assert.Equal("Autosave on · not saved", AutoSaveText.For(true, failed, Now.AddHours(-1), Now));
        Assert.Equal("Autosave on · saved 1 hour ago", AutoSaveText.For(true, oldSaved, null, Now));
    }
}
