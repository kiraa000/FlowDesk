using FlowDesk.App.Services;
using FlowDesk.Core.Model;
using FlowDesk.Core.Palette;

namespace FlowDesk.App.Tests;

public sealed class PaletteFocusDefaultsTests
{
    [Fact] public void DefaultFocusNeedsSetupRatherThanSilentlyBlockingAllApps()
    {
        var settings = new FocusSettings();
        bool canStart = UxDecisions.CanStartFocus(false, settings.BlockAllExceptAllowed, settings.Blocked.Count);
        Assert.False(canStart);
        var item = Assert.Single(PaletteFocusCommands.Create(new(false, false, canStart, false, false),
            _ => throw new Exception(), () => Task.CompletedTask, () => throw new Exception()));
        Assert.Equal("action:focus-setup", item.Id);
        Assert.False(settings.BlockAllExceptAllowed); Assert.Empty(settings.Blocked);
    }
}
