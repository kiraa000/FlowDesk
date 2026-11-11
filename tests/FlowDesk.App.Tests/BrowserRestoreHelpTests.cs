using FlowDesk.App.Services;
using FlowDesk.Core.Model;

namespace FlowDesk.App.Tests;

public sealed class BrowserRestoreHelpTests
{
    [Fact]
    public void BrowserItemDetectionDoesNotDependOnTheToggle()
    {
        var browser = new WorkspaceItem
        {
            Kind = ItemKind.App, App = new AppRef { ExePath = @"C:\Program Files\Microsoft\Edge\msedge.exe" },
            ReopenLastTabs = false,
        };
        Assert.True(BrowserRestoreHelp.IsBrowserItem(browser));

        browser.Kind = ItemKind.Website;
        Assert.False(BrowserRestoreHelp.IsBrowserItem(browser));
    }

    [Fact]
    public void NonBrowsersAndPackagedAppsAreNotBrowserItems()
    {
        var notepad = new WorkspaceItem { Kind = ItemKind.App, App = new AppRef { ExePath = @"C:\Windows\notepad.exe" } };
        var packaged = new WorkspaceItem { Kind = ItemKind.App, App = new AppRef { Aumid = "MSEdge" } };
        Assert.False(BrowserRestoreHelp.IsBrowserItem(notepad));
        Assert.False(BrowserRestoreHelp.IsBrowserItem(packaged));
    }

    [Theory]
    [InlineData("msedge.exe", "msedge", "Settings > Start, home, and new tabs > Open tabs from the previous session")]
    [InlineData("chrome.exe", "chrome", "Settings > On startup > Continue where you left off")]
    [InlineData("firefox.exe", "firefox", "Settings > General > Open previous windows and tabs")]
    public void KnownBrowsersGiveTheExactSetting(string exe, string key, string instruction)
    {
        var notice = BrowserRestoreHelp.For(exe, "Browser");
        Assert.Equal(key, notice.Key);
        Assert.Contains(instruction, notice.Instructions);
    }

    [Fact]
    public void OtherBrowserFamiliesUseGenericGuidanceNotUnsupportedExactSteps()
    {
        var brave = BrowserRestoreHelp.For("brave.exe", "Brave");
        Assert.Equal("exe:brave", brave.Key);
        Assert.Contains("startup settings", brave.Instructions);
        Assert.DoesNotContain("Settings >", brave.Instructions);

        var librewolf = BrowserRestoreHelp.For("librewolf.exe", "LibreWolf");
        Assert.Contains("General or Startup settings", librewolf.Instructions);
        Assert.DoesNotContain("Settings >", librewolf.Instructions);
    }

    [Fact]
    public void NoticeExplainsBrowserOwnSettingAndLimits()
    {
        Assert.Contains("browser must restore its own tabs", BrowserRestoreHelp.Caveat);
        Assert.Contains("not already running", BrowserRestoreHelp.Caveat);
        Assert.Contains("cannot read tabs", BrowserRestoreHelp.Caveat);
    }
}
