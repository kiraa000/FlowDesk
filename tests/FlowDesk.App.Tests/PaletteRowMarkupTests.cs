using System.Xml.Linq;

namespace FlowDesk.App.Tests;

internal static class PaletteMarkupFixture
{
    internal static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    internal static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";
    internal static XDocument Load() => XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", "CommandPaletteWindow.xaml"));
}

// Structural regression guards only: these do not claim native rendering/visual verification.
public sealed class PaletteRowMarkupTests
{
    [Fact] public void ActionPanelReusesResultsAndConfirmationHasAVisibleTextAlternative()
    {
        var doc = PaletteMarkupFixture.Load();
        Assert.Single(doc.Descendants(PaletteMarkupFixture.Xaml + "ListView"));
        var actions = doc.Descendants(PaletteMarkupFixture.Xaml + "Button")
            .Single(e => (string?)e.Attribute(PaletteMarkupFixture.X + "Name") == "ActionsButton");
        Assert.Contains("Ctrl+K", (string?)actions.Attribute("Content"));
        var caution = doc.Descendants(PaletteMarkupFixture.Xaml + "TextBlock")
            .Single(e => (string?)e.Attribute("Text") == "{Binding ConfirmationPrompt}");
        Assert.Equal("{ThemeResource SystemFillColorCautionBrush}", (string?)caution.Attribute("Foreground"));
    }
    [Fact] public void TitlesAndPathsOverrideWrappingInheritedFromWinUiTypeRamp()
    {
        var blocks = PaletteMarkupFixture.Load().Descendants(PaletteMarkupFixture.Xaml + "TextBlock").ToArray();
        var title = blocks.Single(e => (string?)e.Attribute(PaletteMarkupFixture.X + "Name") == "ResultTitle");
        var subtitle = blocks.Single(e => (string?)e.Attribute("Text") == "{Binding Subtitle}");
        foreach (var block in new[] { title, subtitle })
        {
            Assert.Equal("NoWrap", (string?)block.Attribute("TextWrapping"));
            Assert.Equal("1", (string?)block.Attribute("MaxLines"));
            Assert.Equal("CharacterEllipsis", (string?)block.Attribute("TextTrimming"));
            Assert.NotNull(block.Attribute("ToolTipService.ToolTip"));
        }
    }
    [Fact] public void PaletteRowsKeepTheirDesignedHeight()
    {
        var document = PaletteMarkupFixture.Load();
        var style = document.Descendants(PaletteMarkupFixture.Xaml + "Style")
            .Single(e => (string?)e.Attribute(PaletteMarkupFixture.X + "Key") == "PaletteResultItemStyle");
        var height = style.Elements(PaletteMarkupFixture.Xaml + "Setter").Single(e => (string?)e.Attribute("Property") == "Height");
        Assert.Equal("52", (string?)height.Attribute("Value"));
        var template = document.Descendants(PaletteMarkupFixture.Xaml + "DataTemplate").Single();
        Assert.Equal("44", (string?)template.Element(PaletteMarkupFixture.Xaml + "Grid")?.Attribute("Height"));
    }
}
