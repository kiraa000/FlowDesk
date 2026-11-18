using FlowDesk.Core.QuickLook;

namespace FlowDesk.Core.Tests;

public sealed class PaletteQuickLookTests
{
    [Fact] public void ExplicitPreviewRequiresEnabledFeatureAndCurrentOwner()
    {
        var preview = new QuickLookPathSession();
        Assert.False(preview.Begin(@"D:\note.txt", 1, 1, false));
        Assert.False(preview.Begin(@"D:\note.txt", 1, 2, true));
        Assert.False(preview.Begin("relative.txt", 1, 1, true));
        Assert.False(preview.Active);
        Assert.True(preview.Begin(@"D:\note.txt", 1, 1, true));
        Assert.True(preview.BelongsTo(1)); Assert.False(preview.BelongsTo(2));
        Assert.Equal(@"D:\note.txt", preview.Path); preview.Clear(); Assert.False(preview.Active);
    }
}
