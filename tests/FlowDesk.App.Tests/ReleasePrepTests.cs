using System.Xml.Linq;
using FlowDesk.App.Services;

namespace FlowDesk.App.Tests;

public sealed class ReleasePrepTests
{
    private static string Source(params string[] path) => File.ReadAllText(Path.Combine(
        new[] { AppContext.BaseDirectory, "AppSources" }.Concat(path).ToArray()));

    [Fact]
    public void UpdatesLinkIsThePublicHttpsReleasePage()
    {
        var uri = new Uri(AppLinks.Releases, UriKind.Absolute);
        Assert.Equal(Uri.UriSchemeHttps, uri.Scheme);
        Assert.Equal("github.com", uri.Host);
        Assert.Equal("/kiraa000/FlowDesk/releases", uri.AbsolutePath);
        Assert.Empty(uri.UserInfo);
        Assert.Empty(uri.Query);
    }

    [Fact]
    public void AboutKeepsLogsAndUsesTheExistingShellLinkLauncher()
    {
        var page = XDocument.Parse(Source("Views", "SettingsPage.xaml"));
        var about = page.Descendants().Single(e => e.Name.LocalName == "SettingsCard" && (string?)e.Attribute("Header") == "About");
        Assert.Contains(about.Descendants(), e => (string?)e.Attribute("Command") == "{Binding OpenLogFolderCommand}");
        Assert.Contains(about.Descendants(), e => e.Name.LocalName == "HyperlinkButton"
            && (string?)e.Attribute("Content") == "Release notes and updates"
            && (string?)e.Attribute("Command") == "{Binding OpenReleaseNotesCommand}");
        Assert.Contains("AdvancedToolService.OpenLink(AppLinks.Releases)", Source("ViewModels", "PageViewModels.cs"));
    }

    [Fact]
    public void CalmDisplayNameDoesNotChangeNavigationOrTourIdentity()
    {
        var navigation = XDocument.Parse(Source("MainWindow.xaml"));
        var calm = navigation.Descendants().Single(e => e.Name.LocalName == "NavigationViewItem" && (string?)e.Attribute("Tag") == "Calm");
        Assert.Equal("Calm Windows", (string?)calm.Attribute("Content"));
        Assert.Contains("FindNavItem(\"Calm\")", Source("MainWindow.Tour.cs"));
        Assert.Contains("item.Tag?.ToString() == tag", Source("MainWindow.Tour.cs"));
        Assert.Contains("on the Calm Windows page", Source("Tour", "TourStateMachine.cs"));
        Assert.Contains("Text=\"Calm Windows\"", Source("Views", "CalmPage.xaml"));
        var palette = Source("Services", "CommandPaletteService.cs");
        Assert.Contains("\"page-\" + page", palette); // Preserve command/history identity.
        Assert.Contains("page == AppPage.Calm ? \"Calm Windows\" : page.ToString()", palette);
    }

    [Fact]
    public void WindowShortcutsLinkReusesHotkeysNavigationAndFollowsTheSwitch()
    {
        var page = XDocument.Parse(Source("Views", "SettingsPage.xaml"));
        var link = page.Descendants().Single(e => e.Name.LocalName == "HyperlinkButton" && (string?)e.Attribute("Content") == "Open Hotkeys");
        Assert.Equal("{Binding ManageShortcutsCommand}", (string?)link.Attribute("Command"));
        Assert.Contains("Binding WindowShortcuts,", (string?)link.Attribute("Visibility"));
        Assert.Contains("ManageShortcuts() => owner.Navigate(AppPage.Hotkeys)", Source("ViewModels", "PageViewModels.cs"));
    }
}
