using System.Xml.Linq;

namespace FlowDesk.App.Tests;

public sealed class PaletteSurfaceMarkupTests
{
    [Fact] public void NormalThemesExposeTheSystemBackdropButHighContrastUsesASolidSystemSurface()
    {
        // App.xaml, not the window: the Root Grid's Background is resolved before its own Grid.Resources exist,
        // so a key defined in the window crashed start-up ("Cannot find a Resource ... PaletteSurfaceBackgroundBrush").
        var palette = PaletteMarkupFixture.Load();
        Assert.DoesNotContain(palette.Descendants(PaletteMarkupFixture.Xaml + "SolidColorBrush"),
            e => (string?)e.Attribute(PaletteMarkupFixture.X + "Key") == "PaletteSurfaceBackgroundBrush");
        var document = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", "App.xaml"));
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
        Assert.Equal("{ThemeResource SurfaceStrokeColorFlyoutBrush}", (string?)root.Attribute("BorderBrush"));
        Assert.Equal("1", (string?)root.Attribute("BorderThickness"));
        var surface = root.Descendants(PaletteMarkupFixture.Xaml + "Border").Single(e => (string?)e.Attribute("Grid.Row") == "1");
        Assert.Equal("ContentSurface", (string?)surface.Attribute(PaletteMarkupFixture.X + "Name"));
        Assert.Equal("{ThemeResource SolidBackgroundFillColorBaseBrush}", (string?)surface.Attribute("Background"));
        Assert.Equal("Stretch", (string?)surface.Attribute("HorizontalAlignment"));
        var list = surface.Descendants(PaletteMarkupFixture.Xaml + "ListView").Single();
        var results = list.Ancestors(PaletteMarkupFixture.Xaml + "Border").First();
        Assert.Equal("Transparent", (string?)results.Attribute("Background"));
        Assert.Equal("0", (string?)results.Attribute("BorderThickness"));
    }

    [Fact] public void SolidFallbackCoversOnlyTheSurfaceAndCannotInterceptInput()
    {
        var root = PaletteMarkupFixture.Load().Descendants(PaletteMarkupFixture.Xaml + "Grid")
            .Single(e => (string?)e.Attribute(PaletteMarkupFixture.X + "Name") == "Root");
        var fallback = root.Elements(PaletteMarkupFixture.Xaml + "Border").Single();
        Assert.Equal("SolidFallback", (string?)fallback.Attribute(PaletteMarkupFixture.X + "Name"));
        Assert.Equal("{ThemeResource SolidBackgroundFillColorBaseBrush}", (string?)fallback.Attribute("Background"));
        Assert.Equal("Collapsed", (string?)fallback.Attribute("Visibility"));
        Assert.Equal("False", (string?)fallback.Attribute("IsHitTestVisible"));
        var content = root.Elements(PaletteMarkupFixture.Xaml + "Grid").Single();
        Assert.True(XNode.DocumentOrderComparer.Compare(fallback, content) < 0);
        Assert.Equal("12", (string?)content.Attribute("Padding"));
        Assert.Null(root.Attribute("Padding"));
    }

    [Fact] public void ActionAndKeyboardHintsUseTheSecondaryContrastBudget()
    {
        var hints = PaletteMarkupFixture.Load().Descendants(PaletteMarkupFixture.Xaml + "TextBlock")
            .Where(e => (string?)e.Attribute("Text") == "{Binding Hint}" ||
                ((string?)e.Attribute("Text"))?.Contains("to select", StringComparison.Ordinal) == true).ToArray();
        Assert.Equal(2, hints.Length);
        Assert.All(hints, hint => Assert.Equal("{ThemeResource TextFillColorSecondaryBrush}", (string?)hint.Attribute("Foreground")));
    }
}
