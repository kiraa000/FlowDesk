using FlowDesk.App.Services;

namespace FlowDesk.App.Tests;

public sealed class PaletteWindowStyleTests
{
    [Theory]
    [InlineData(0x04480000u, 0x84000000u)] // proven hidden presenter style
    [InlineData(0x14480000u, 0x94000000u)] // proven shown style; retain WS_VISIBLE
    [InlineData(0x04CF0000u, 0x84030000u)] // retain unrelated bits, remove every native frame bit
    [InlineData(0u, 0x80000000u)]
    [InlineData(0x94000000u, 0x94000000u)]
    public void RemovesNativeFrameAndAddsPopupWithoutChangingOtherBits(uint original, uint expected)
    {
        uint result = PaletteWindowStyle.Apply(original);
        Assert.Equal(expected, result);
        Assert.Equal(0u, result & PaletteWindowStyle.NativeFrame);
        Assert.Equal(original & ~(PaletteWindowStyle.NativeFrame | PaletteWindowStyle.Popup),
            result & ~(PaletteWindowStyle.NativeFrame | PaletteWindowStyle.Popup));
        Assert.Equal(result, PaletteWindowStyle.Apply(result));
    }
}
