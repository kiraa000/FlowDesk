using FlowDesk.App.Services;

namespace FlowDesk.App.Tests;

public sealed class PaletteBackdropPolicyTests
{
    [Fact]
    public void OnlyAnActiveSupportedMaterialWithEffectsAndNoHighContrastExposesAcrylic()
    {
        foreach (bool ready in new[] { false, true })
        foreach (bool active in new[] { false, true })
        foreach (bool highContrast in new[] { false, true })
        foreach (bool effects in new[] { false, true })
            Assert.Equal(!(ready && active && !highContrast && effects),
                PaletteBackdropPolicy.UseSolidFallback(ready, active, highContrast, effects));
    }
}
