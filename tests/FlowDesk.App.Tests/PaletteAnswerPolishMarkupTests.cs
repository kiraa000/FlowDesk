using System.Xml.Linq;

namespace FlowDesk.App.Tests;

public sealed class PaletteAnswerPolishMarkupTests
{
    [Fact] public void ResultsAndAnswerShareOneFullWidthNeutralSurface()
    {
        var doc = PaletteMarkupFixture.Load();
        var surface = doc.Descendants().Single(e => (string?)e.Attribute(PaletteMarkupFixture.X + "Name") == "ContentSurface");
        var answer = surface.Descendants().Single(e => (string?)e.Attribute(PaletteMarkupFixture.X + "Name") == "AnswerPane");
        Assert.Contains(surface.Descendants(), e => (string?)e.Attribute(PaletteMarkupFixture.X + "Name") == "Results");
        Assert.Equal("Stretch", (string?)answer.Attribute("HorizontalAlignment")); Assert.Null(answer.Attribute("Grid.Row"));
        var scroll = answer.Descendants().Single(e => (string?)e.Attribute(PaletteMarkupFixture.X + "Name") == "AnswerScroll");
        Assert.Equal("Stretch", (string?)scroll.Attribute("HorizontalContentAlignment"));
    }
    [Fact] public void FallbackToggleHasASeparateSettingsCardHeaderRatherThanAnOverlappingToggleHeader()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "AppSources", "Views", "AiSettingsPanel.xaml");
        var doc = XDocument.Load(path);
        var toggle = doc.Descendants().Single(e => (string?)e.Attribute(PaletteMarkupFixture.X + "Name") == "FallbackSwitch");
        Assert.Null(toggle.Attribute("Header")); Assert.Equal("SettingsCard", toggle.Parent!.Name.LocalName);
        Assert.Equal("Show an Ask AI row in mixed results", (string?)toggle.Parent.Attribute("Header"));
    }
    [Fact] public void AnswerSizingMeasuresTextAndTypingUsesComposedCharactersNotSyntheticKeys()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "AppSources", "Views");
        string answer = File.ReadAllText(Path.Combine(root, "CommandPaletteWindow.Ai.cs"));
        Assert.Contains("AnswerBody.Measure(available)", answer); Assert.Contains("PaletteAnswerLayout.Height(", answer);
        Assert.DoesNotContain("visible ? 8", answer);
        Assert.Contains("PaletteAnswerInput.StartsSearch(e.Character", answer);
        Assert.Contains("SearchBox.Text = text", answer); Assert.Contains("SearchBox.Focus(FocusState.Programmatic)", answer);
        Assert.Contains("AnswerStatus.Visibility = answer.Status.Length == 0 ? Visibility.Collapsed", answer);
        Assert.Contains("span.Bold", answer); Assert.Contains("span.Italic", answer);
        Assert.Contains("scheduledFocus == answerFocusVersion", answer);
        string window = File.ReadAllText(Path.Combine(root, "CommandPaletteWindow.xaml.cs"));
        Assert.Contains("UIElement.CharacterReceivedEvent", window); Assert.DoesNotContain("ResizeForResults(answer.Visible ? 8", window);
    }
}
