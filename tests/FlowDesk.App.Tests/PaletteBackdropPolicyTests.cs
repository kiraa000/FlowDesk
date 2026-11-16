using FlowDesk.App.Services;

namespace FlowDesk.App.Tests;

public sealed class PaletteBackdropPolicyTests
{
    [Fact]
    public void OnlyExplicitSystemConditionsOrAMissingControllerRequireAnExtraSolidLayer()
    {
        foreach (bool ready in new[] { false, true })
        foreach (bool active in new[] { false, true })
        foreach (bool highContrast in new[] { false, true })
        foreach (bool effects in new[] { false, true })
            Assert.Equal(!ready || highContrast || !effects,
                PaletteBackdropPolicy.UseSolidFallback(ready, active, highContrast, effects));
    }

    [Fact]
    public void OpeningActiveHideAndReopenNeverAddAnOverlayForNativeFallback()
    {
        // No timer/age boundary: even a long native fallback belongs to the controller, not XAML.
        foreach (bool nativeActive in new[] { false, false, true, false, false, true, false })
            Assert.False(PaletteBackdropPolicy.UseSolidFallback(true, nativeActive, false, true));
    }

    [Theory]
    [InlineData(false, false, true)] // unsupported / failed creation
    [InlineData(true, true, true)]  // High Contrast
    [InlineData(true, false, false)] // transparency off
    public void ExplicitCausesDoNotWaitForControllerActivationOrAPersistenceTimer(bool ready, bool highContrast, bool effects)
    {
        Assert.True(PaletteBackdropPolicy.UseSolidFallback(ready, false, highContrast, effects));
        Assert.True(PaletteBackdropPolicy.UseSolidFallback(ready, true, highContrast, effects));
    }

    [Fact]
    public void SystemPolicyChangesRestoreNativeMaterialImmediatelyInEitherNativeState()
    {
        foreach (bool active in new[] { false, true })
        {
            Assert.True(PaletteBackdropPolicy.UseSolidFallback(true, active, false, false));
            Assert.False(PaletteBackdropPolicy.UseSolidFallback(true, active, false, true));
            Assert.True(PaletteBackdropPolicy.UseSolidFallback(true, active, true, true));
            Assert.False(PaletteBackdropPolicy.UseSolidFallback(true, active, false, true));
        }
    }
}
