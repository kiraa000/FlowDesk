namespace FlowDesk.App.Tests;

public sealed class PaletteSurfaceMarkupTests
{
    [Fact] public void NormalThemesExposeTheSystemBackdropButHighContrastUsesASolidSystemSurface()
    {
        var document = PaletteMarkupFixture.Load();
        var dictionaries = document.Descendants(PaletteMarkupFixture.Xaml + "ResourceDictionary")
            .Where(e => e.Attribute(PaletteMarkupFixture.X + "Key") is not null)
            .ToDictionary(e => (string)e.Attribute(PaletteMarkupFixture.X + "Key")!);
        foreach (var theme in new[] { "Default", "Light", "HighContrast" })
        {
            var brush = dictionaries[theme].Elements(PaletteMarkupFixture.Xaml + "SolidColorBrush")
                .Single(e => (string?)e.Attribute(PaletteMarkupFixture.X + "Key") == "PaletteSurfaceBackgroundBrush");
            Assert.Equal(theme == "HighContrast" ? "{ThemeResource SystemColorWindowColor}" : "Transparent", (string?)brush.Attribute("Color"));
        }
    }
    [Fact] public void OneFluentSurfaceStrokeReplacesNestedCardOutlines()
    {
        var document = PaletteMarkupFixture.Load();
        var root = document.Descendants(PaletteMarkupFixture.Xaml + "Grid")
            .Single(e => (string?)e.Attribute(PaletteMarkupFixture.X + "Name") == "Root");
        Assert.Equal("{ThemeResource SurfaceStrokeColorDefaultBrush}", (string?)root.Attribute("BorderBrush"));
        Assert.Equal("1", (string?)root.Attribute("BorderThickness"));
        var results = root.Elements(PaletteMarkupFixture.Xaml + "Border").Single();
        Assert.Equal("Transparent", (string?)results.Attribute("Background"));
        Assert.Equal("0", (string?)results.Attribute("BorderThickness"));
    }
}
