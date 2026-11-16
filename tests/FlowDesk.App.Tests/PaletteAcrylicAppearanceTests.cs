using FlowDesk.App.Services;
using Xunit.Abstractions;

namespace FlowDesk.App.Tests;

public sealed class PaletteAcrylicAppearanceTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false, 0xFC, 0.0, 0.85, 0xF9)]
    [InlineData(true, 0x2C, 0.15, 0.80, 0x2C)]
    public void UsesWinUiNeutralFlyoutColoursWithoutAnOpaqueMaterial(bool dark, byte tint, double tintOpacity, double luminosity, byte fallback)
    {
        var profile = PaletteAcrylicAppearance.For(dark);
        Assert.Equal(tint, profile.TintGray);
        Assert.Equal(tintOpacity, profile.TintOpacity, 5);
        Assert.Equal(luminosity, profile.LuminosityOpacity, 5);
        Assert.Equal(fallback, profile.FallbackGray);
        Assert.InRange(profile.LuminosityOpacity, 0.01f, 0.99f);
        Assert.InRange(profile.TintOpacity, 0f, 0.99f);
    }

    [Theory]
    [InlineData(false, 0, 0x9E, 0)]
    [InlineData(true, 255, 0xC5, 255)]
    public void SecondaryTextPassesContrastModelOverWorstCaseMonochromeBackdrop(bool dark, byte behind, byte textAlpha, byte textGray)
    {
        var profile = PaletteAcrylicAppearance.For(dark);
        // WinUI AcrylicBrush.cpp: luminosity blend then colour blend, then 2% source-over noise.
        // With neutral inputs the colour blend does not change luminosity. Do NOT model TintOpacity
        // as an extra source-over white/black wash. This reference-recipe model is NOT a measurement
        // of the closed native DesktopAcrylicController renderer; Opus must verify actual pixels.
        double surface = profile.TintGray * profile.LuminosityOpacity + behind * (1 - profile.LuminosityOpacity);
        double noisy = surface * 0.98 + (dark ? 255 : 0) * 0.02;
        double roundedSurface = dark ? Math.Ceiling(noisy) : Math.Floor(noisy);
        double text = textGray * (textAlpha / 255.0) + roundedSurface * (1 - textAlpha / 255.0);
        double roundedText = dark ? Math.Floor(text) : Math.Ceiling(text);
        double contrast = Contrast(roundedSurface, roundedText);
        output.WriteLine($"{(dark ? "Dark/white" : "Light/black")}: model surface={surface:F2}, noise/round worst={roundedSurface:F0}, text={roundedText:F0}, contrast={contrast:F3}:1");
        Assert.True(contrast >= 4.5, $"Contrast model {contrast:F3}:1 is below 4.5:1");
        // The model must retain meaningful background variation, rather than solving contrast at opacity=1.
        Assert.InRange(255 * (1 - profile.LuminosityOpacity), 30, 60);
    }

    private static double Contrast(double first, double second) =>
        (Math.Max(Luminance(first), Luminance(second)) + 0.05) / (Math.Min(Luminance(first), Luminance(second)) + 0.05);
    private static double Luminance(double gray)
    {
        double s = gray / 255;
        return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
    }
}
