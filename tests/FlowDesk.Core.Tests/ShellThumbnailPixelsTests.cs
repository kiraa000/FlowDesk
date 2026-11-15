using FlowDesk.Core.QuickLook;

namespace FlowDesk.Core.Tests;

public sealed class ShellThumbnailPixelsTests
{
    [Fact] public void PreservesTransparentAndUnpremultipliesSemitransparentPixels()
    {
        byte[] result = ShellThumbnailPixels.StraightBgra([0, 0, 0, 0, 32, 64, 128, 128], 2, 1, 8, true);
        Assert.Equal(new byte[] { 0, 0, 0, 0, 64, 128, 255, 128 }, result);
    }
    [Fact] public void LegacyZeroAlphaIsOpaqueAndPaddingIsSkipped()
    {
        byte[] result = ShellThumbnailPixels.StraightBgra([1, 2, 3, 0, 9, 9, 9, 9, 4, 5, 6, 0, 8, 8, 8, 8], 1, 2, 8, false);
        Assert.Equal(new byte[] { 4, 5, 6, 255, 1, 2, 3, 255 }, result);
    }
    [Fact] public void OpaquePixelsAreUnchanged() => Assert.Equal(new byte[] { 1, 2, 3, 255 },
        ShellThumbnailPixels.StraightBgra([1, 2, 3, 255], 1, 1, 4, true));
    [Fact] public void RejectsInvalidSize() => Assert.Throws<ArgumentException>(() => ShellThumbnailPixels.StraightBgra([0], 1, 1, 4, true));
}
