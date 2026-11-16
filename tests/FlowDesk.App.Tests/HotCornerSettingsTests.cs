using System.Text.Json;
using FlowDesk.App.Services;
using FlowDesk.Core.HotCorners;

namespace FlowDesk.App.Tests;
public sealed class HotCornerSettingsTests
{
    [Fact] public void OldFilesLoadOffWithDefaultMapping()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>("{\"QuickLookEnabled\":true}")!;
        Assert.False(settings.HotCornersEnabled); Assert.True(settings.QuickLookEnabled);
        Assert.Equal(HotCornerAction.TaskView, settings.HotCornerTopLeft);
        Assert.Equal(HotCornerAction.ShowDesktop, settings.HotCornerBottomRight);
        Assert.Equal(HotCornerAction.None, settings.HotCornerTopRight); Assert.Equal(HotCornerAction.None, settings.HotCornerBottomLeft);
    }
    [Fact] public void MappingsRoundTripWithoutChangingOtherModuleToggles()
    {
        var value = new AppSettings { HotCornersEnabled = true, HotCornerTopLeft = HotCornerAction.Lock, HotCornerTopRight = HotCornerAction.StartFocus,
            HotCornerBottomLeft = HotCornerAction.TaskView, HotCornerBottomRight = HotCornerAction.None };
        var result = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(value))!;
        Assert.True(result.HotCornersEnabled); Assert.False(result.WindowShortcutsEnabled); Assert.False(result.QuickLookEnabled);
        Assert.Equal(value.HotCornerTopLeft, result.HotCornerTopLeft); Assert.Equal(value.HotCornerTopRight, result.HotCornerTopRight);
        Assert.Equal(value.HotCornerBottomLeft, result.HotCornerBottomLeft); Assert.Equal(value.HotCornerBottomRight, result.HotCornerBottomRight);
        Assert.Equal(new[] { "None", "Task View", "Show desktop", "Start Focus", "Lock", "Open command palette" }, HotCornerConfiguration.Actions.Select(x => x.Label));
    }
}
