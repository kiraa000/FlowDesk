using FlowDesk.Core.Hotkeys;

namespace FlowDesk.Core.Tests;

public sealed class ShortcutStatusDetailTests
{
    [Theory]
    [InlineData(ShortcutStatus.Active, false, false, "Active")]
    [InlineData(ShortcutStatus.Off, true, false, "Off")]
    [InlineData(ShortcutStatus.Conflict, true, true, "In use by another app")]
    [InlineData(ShortcutStatus.Unavailable, true, false, "Not available")]
    public void CompactRowsHideOnlyActiveAndEmphasizeOnlyConflicts(ShortcutStatus status, bool showDetail, bool caution, string text)
    {
        Assert.Equal(showDetail, ShortcutPresentation.ShowStatusDetail(status));
        Assert.Equal(caution, ShortcutPresentation.IsConflict(status));
        Assert.Equal(text, ShortcutPresentation.StatusText(status));
    }
}
