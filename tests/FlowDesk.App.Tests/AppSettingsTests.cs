using System.Text.Json;
using FlowDesk.App.Services;

namespace FlowDesk.App.Tests;

public sealed class AppSettingsTests
{
    [Fact]
    public void SettingsFromBeforeTask004KeepAutosaveOnAndAlwaysOff()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>("""
            { "Theme": "Dark", "MinimizePrevious": false, "LaunchTimeoutSeconds": 35 }
            """)!;

        Assert.Equal("Dark", settings.Theme);
        Assert.True(settings.IsAutosaveEnabled("workspace-1"));
        Assert.False(settings.AlwaysAddsNewWindows("workspace-1"));
        Assert.False(settings.HasSeenBrowserNotice("msedge"));
    }

    [Fact]
    public void AutosaveAndAlwaysAddArePerWorkspace()
    {
        var settings = new AppSettings();
        settings.SetAutosaveEnabled("a", false);
        settings.SetAlwaysAddNewWindows("a", true);

        Assert.False(settings.IsAutosaveEnabled("a"));
        Assert.True(settings.IsAutosaveEnabled("b"));
        Assert.True(settings.AlwaysAddsNewWindows("a"));
        Assert.False(settings.AlwaysAddsNewWindows("b"));

        settings.SetAutosaveEnabled("a", true);
        settings.SetAlwaysAddNewWindows("a", false);
        Assert.True(settings.IsAutosaveEnabled("a"));
        Assert.False(settings.AlwaysAddsNewWindows("a"));
        Assert.Empty(settings.AutosaveOff);
        Assert.Empty(settings.AlwaysAddNewWindows);
    }

    [Fact]
    public void BrowserNoticeAcknowledgementIsStoredOnce()
    {
        var settings = new AppSettings();
        settings.MarkBrowserNoticeSeen("chrome");
        settings.MarkBrowserNoticeSeen("chrome");
        Assert.True(settings.HasSeenBrowserNotice("chrome"));
        Assert.False(settings.HasSeenBrowserNotice("firefox"));
        Assert.Single(settings.BrowserRestoreNoticesSeen);

        var roundTrip = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        Assert.True(roundTrip.HasSeenBrowserNotice("chrome"));
    }
}
