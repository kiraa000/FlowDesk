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
        foreach (string header in new[] { "Command palette", "Open with the Windows key", "File search", "Run history" })
            Assert.Contains(palette.Descendants(), e => (string?)e.Attribute("Header") == header);
        // SettingsExpander styles each item as a SettingsCard; a nested SettingsExpander item crashes on expand
        // (COMException 0x800F1000 in ItemsRepeater_ElementPrepared, live test 2026-11-18).
        foreach (var items in elements.Where(e => e.Name.LocalName == "SettingsExpander.Items"))
            Assert.All(items.Elements(), item => Assert.Equal("SettingsCard", item.Name.LocalName));
        foreach (string header in new[] { "Custom commands", "AI answers" })
            Assert.Contains(elements, e => e.Name.LocalName == "SettingsExpander" && (string?)e.Attribute("Header") == header);
        Assert.Contains("FocusAdvancedSetting()", Source("Views", "SettingsPage.xaml.cs"));
    }
    [Fact] public void ActiveMarkerUsesStateNotTagColour()
    {
        var document = XDocument.Parse(Source("Views", "WorkspacesPage.xaml"));
        var active = Assert.Single(document.Descendants(), e => (string?)e.Attribute("Text") == "Active");
        Assert.Contains("IsActive", (string)active.Attribute("Visibility")!);
    }
}
