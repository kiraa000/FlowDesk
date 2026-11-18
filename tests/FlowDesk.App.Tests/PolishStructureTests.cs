using System.Xml.Linq;

namespace FlowDesk.App.Tests;

public sealed class PolishStructureTests
{
    private static string Source(params string[] path) => File.ReadAllText(Path.Combine(new[] { AppContext.BaseDirectory, "AppSources" }.Concat(path).ToArray()));
    [Fact] public void SettingsGroupsKeepAdvancedFocusOutsideCollapsedOptions()
    {
        var document = XDocument.Parse(Source("Views", "SettingsPage.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var elements = document.Descendants().ToArray();
        foreach (string section in new[] { "General", "Workspaces", "Shortcuts", "Command palette", "Quick Look & hot corners", "Clipboard history", "Data", "About" })
            Assert.Contains(elements, e => e.Name.LocalName == "TextBlock" && (string?)e.Attribute("Text") == section);
        var advanced = Assert.Single(elements, e => (string?)e.Attribute(x + "Name") == "AdvancedModeSwitch");
        Assert.DoesNotContain(advanced.Ancestors(), e => e.Name.LocalName == "SettingsExpander");
        var palette = Assert.Single(elements, e => e.Name.LocalName == "SettingsExpander" && (string?)e.Attribute("Header") == "Command palette options");
        foreach (string header in new[] { "Command palette", "Open with the Windows key", "File search", "Custom commands", "AI answers", "Run history" })
            Assert.Contains(palette.Descendants(), e => (string?)e.Attribute("Header") == header);
        Assert.Contains("FocusAdvancedSetting()", Source("Views", "SettingsPage.xaml.cs"));
    }
    [Fact] public void ActiveMarkerUsesStateNotTagColour()
    {
        var document = XDocument.Parse(Source("Views", "WorkspacesPage.xaml"));
        var active = Assert.Single(document.Descendants(), e => (string?)e.Attribute("Text") == "Active");
        Assert.Contains("IsActive", (string)active.Attribute("Visibility")!);
    }
}
