namespace FlowDesk.App.Services;

/// <summary>Neutral WinUI flyout colours with bounded luminosity, not an opaque XAML overlay.</summary>
internal readonly record struct PaletteAcrylicAppearance(byte TintGray, float TintOpacity, float LuminosityOpacity, byte FallbackGray)
{
    // Pinned WinUI 2.6.5 AcrylicBackgroundFillColorDefaultBrush: Light FC/0/.85/F9;
    // Dark 2C/.15/.96/2C. Lower Dark luminosity opacity to .80 to retain visible backdrop variation.
    // Both stay below 1; pure black/white contrast modelling is in PaletteAcrylicAppearanceTests.
    public static PaletteAcrylicAppearance For(bool dark) => dark
        ? new(0x2C, 0.15f, 0.80f, 0x2C)
        : new(0xFC, 0.0f, 0.85f, 0xF9);
}
