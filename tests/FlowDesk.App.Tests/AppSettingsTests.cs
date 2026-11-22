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
    public void ActiveWorkspaceIdIsPersistedAndOlderSettingsHaveNone()
    {
        var older = JsonSerializer.Deserialize<AppSettings>("""{ "Theme": "Light" }""")!;
        Assert.Null(older.ActiveWorkspaceId);

        older.ActiveWorkspaceId = "workspace-1";
        var roundTrip = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(older))!;
        Assert.Equal("workspace-1", roundTrip.ActiveWorkspaceId);

        roundTrip.ActiveWorkspaceId = null;
        var cleared = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(roundTrip))!;
        Assert.Null(cleared.ActiveWorkspaceId);
    }

    [Fact]
    public void DeletingTheActiveWorkspaceClearsOnlyThatIdAndSurvivesRoundTrip()
    {
        var settings = new AppSettings { ActiveWorkspaceId = "active" };

        Assert.False(settings.ClearActiveWorkspaceIfMatches("other"));
        Assert.Equal("active", settings.ActiveWorkspaceId);

        Assert.True(settings.ClearActiveWorkspaceIfMatches("active"));
        Assert.Null(settings.ActiveWorkspaceId);
        Assert.False(settings.ClearActiveWorkspaceIfMatches("active"));

        var roundTrip = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        Assert.Null(roundTrip.ActiveWorkspaceId);
        Assert.False(roundTrip.ClearActiveWorkspaceIfMatches("active"));
    }

    [Fact]
    public void WindowShortcutsAreOffByDefaultAndForOlderSettingsFiles()
    {
        Assert.False(new AppSettings().WindowShortcutsEnabled);

        var older = JsonSerializer.Deserialize<AppSettings>("""{ "Theme": "Dark" }""")!;
        Assert.False(older.WindowShortcutsEnabled);
    }

    [Fact]
    public void WindowShortcutsRoundTrip()
    {
        var settings = new AppSettings { WindowShortcutsEnabled = true };
        var roundTrip = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        Assert.True(roundTrip.WindowShortcutsEnabled);
    }

    [Fact]
    public void ResizeTouchingWindowsIsOnByDefaultAndForOlderSettingsFiles()
    {
        Assert.True(new AppSettings().ResizeTouchingWindowsTogether);

        var older = JsonSerializer.Deserialize<AppSettings>("""{ "Theme": "Dark" }""")!;
        Assert.True(older.ResizeTouchingWindowsTogether);
    }

    [Fact]
    public void ResizeTouchingWindowsRoundTripsWhenTurnedOff()
    {
        var settings = new AppSettings { ResizeTouchingWindowsTogether = false };
        string json = JsonSerializer.Serialize(settings);
        Assert.Contains("\"ResizeTouchingWindowsTogether\":false", json);

        var roundTrip = JsonSerializer.Deserialize<AppSettings>(json)!;
        Assert.False(roundTrip.ResizeTouchingWindowsTogether);
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

    [Fact]
    public void SavedFileFormatLoadsBackIncludingEnumStrings()
    {
        var settings = new AppSettings
        {
            WindowShortcutsEnabled = true,
            HotCornersEnabled = true,
            HotCornerTopRight = FlowDesk.Core.HotCorners.HotCornerAction.Lock,
            DisabledShortcuts = ["desktop-switch-3"],
        };
        string json = settings.ToJson();
        Assert.Contains("\"TaskView\"", json);

        var loaded = AppSettings.FromJson(json);
        Assert.True(loaded.WindowShortcutsEnabled);
        Assert.True(loaded.HotCornersEnabled);
        Assert.Equal(FlowDesk.Core.HotCorners.HotCornerAction.TaskView, loaded.HotCornerTopLeft);
        Assert.Equal(FlowDesk.Core.HotCorners.HotCornerAction.Lock, loaded.HotCornerTopRight);
        Assert.Equal(["desktop-switch-3"], loaded.DisabledShortcuts);
    }

    [Fact]
    public void LiveFileWrittenByTask014LoadsWithoutFallingBackToDefaults()
    {
        var loaded = AppSettings.FromJson("""
            { "Theme": "Dark", "WindowShortcutsEnabled": true, "DisabledShortcuts": ["desktop-switch-3"],
              "HotCornersEnabled": false, "HotCornerTopLeft": "TaskView", "HotCornerTopRight": "None",
              "HotCornerBottomLeft": "None", "HotCornerBottomRight": "ShowDesktop" }
            """);
        Assert.Equal("Dark", loaded.Theme);
        Assert.True(loaded.WindowShortcutsEnabled);
        Assert.Equal(FlowDesk.Core.HotCorners.HotCornerAction.ShowDesktop, loaded.HotCornerBottomRight);
    }
}
