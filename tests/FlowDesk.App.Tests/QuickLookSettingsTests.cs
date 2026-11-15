using System.Text.Json;
using FlowDesk.App.Services;

namespace FlowDesk.App.Tests;

public sealed class QuickLookSettingsTests
{
    [Fact] public void OffByDefault() => Assert.False(new AppSettings().QuickLookEnabled);
    [Fact] public void OldFileLoadsWithoutOptingIn()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>("{\"Theme\":\"Dark\",\"WindowShortcutsEnabled\":true}")!;
        Assert.False(settings.QuickLookEnabled); Assert.True(settings.WindowShortcutsEnabled); Assert.Equal("Dark", settings.Theme);
    }
    [Fact] public void EnabledRoundTrips()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(new AppSettings { QuickLookEnabled = true }))!;
        Assert.True(settings.QuickLookEnabled);
    }
}
