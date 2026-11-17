using System.Xml.Linq;

namespace FlowDesk.App.Tests;

public sealed class HotkeysRowMarkupTests
{
    [Fact] public void LabelAndKeyCapsShareTheFirstRowWithToggleAtTheRightEdge()
    {
        var row = RowGrid();
        Assert.Equal(new[] { "*", "Auto", "Auto", "40" }, row.Element(PaletteMarkupFixture.Xaml + "Grid.ColumnDefinitions")!
            .Elements().Select(e => (string?)e.Attribute("Width")));
        var label = row.Elements(PaletteMarkupFixture.Xaml + "TextBlock").Single();
        Assert.Equal("NoWrap", (string?)label.Attribute("TextWrapping"));
        Assert.Equal("CharacterEllipsis", (string?)label.Attribute("TextTrimming"));
        Assert.NotNull(label.Attribute("ToolTipService.ToolTip"));
        var caps = row.Element(PaletteMarkupFixture.Xaml + "ItemsControl")!;
        Assert.Equal("1", (string?)caps.Attribute("Grid.Column"));
        var toggle = row.Element(PaletteMarkupFixture.Xaml + "ToggleSwitch")!;
        Assert.Equal("3", (string?)toggle.Attribute("Grid.Column"));
        Assert.Equal("40", (string?)toggle.Attribute("Width"));
        Assert.Equal("0", (string?)toggle.Attribute("MinWidth")); // native default 154 leaves a large blank tail
        Assert.Equal("Right", (string?)toggle.Attribute("HorizontalAlignment"));
    }
    [Fact] public void ConditionalStatusHasConstrainedTextAndCautionGlyphNotColorAlone()
    {
        var status = RowGrid().Elements(PaletteMarkupFixture.Xaml + "Grid").Single();
        Assert.Equal("0", (string?)status.Attribute("Grid.Column"));
        Assert.Equal("1", (string?)status.Attribute("Grid.Row"));
        Assert.Contains("ShowStatus", (string?)status.Attribute("Visibility") ?? "");
        var icon = status.Element(PaletteMarkupFixture.Xaml + "FontIcon")!;
        Assert.Equal("\uE7BA", (string?)icon.Attribute("Glyph"));
        Assert.Contains("IsConflict", (string?)icon.Attribute("Visibility") ?? "");
        var warningText = status.Elements(PaletteMarkupFixture.Xaml + "TextBlock")
            .Single(e => ((string?)e.Attribute("Visibility"))?.Contains("{Binding IsConflict,") == true);
        Assert.Equal("{ThemeResource SystemFillColorCautionBrush}", (string?)warningText.Attribute("Foreground"));
        Assert.Equal("1", (string?)warningText.Attribute("Grid.Column"));
        Assert.Equal("NoWrap", (string?)warningText.Attribute("TextWrapping"));
    }
    private static XElement RowGrid()
    {
        var xaml = PaletteMarkupFixture.Xaml;
        var document = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", "HotkeysPage.xaml"));
        var rows = document.Descendants(xaml + "ItemsControl").Single(e => (string?)e.Attribute("ItemsSource") == "{Binding Rows}");
        return rows.Element(xaml + "ItemsControl.ItemTemplate")!.Element(xaml + "DataTemplate")!.Element(xaml + "Border")!.Element(xaml + "Grid")!;
    }
}
