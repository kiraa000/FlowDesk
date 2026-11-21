using FlowDesk.Core.Model;
using FlowDesk.Core.Windows;

namespace FlowDesk.Core.Tests;

public sealed class MonitorNamesTests
{
    private static MonitorInfo Screen(string device, int x, int y, bool main = false) =>
        new(device, new(x, y, 1920, 1080), new(x, y, 1920, 1040), main, 96);
    [Fact] public void NumbersFollowPositionNotWindowsSuffixOrMainFlag()
    {
        var names = new MonitorNames();
        var main = Screen("DISPLAY58", 0, 0, true);
        names.Observe([Screen("DISPLAY29", 1920, 0), main, Screen("DISPLAY59", -1920, 0)]);
        Assert.Equal("Display 1", names.For("DISPLAY59"));
        Assert.Equal("Display 2 · 1920 × 1080 · Main", names.Label(main));
        Assert.Equal("Display 3", names.For("DISPLAY29"));
    }
    [Fact] public void TiesUseTopThenDeviceAndDisconnectedDevicesArePruned()
    {
        var names = new MonitorNames();
        names.Observe([Screen("z", 0, 0), Screen("b", 0, -100), Screen("a", 0, -100)]);
        Assert.Equal("Display 1", names.For("a")); Assert.Equal("Display 2", names.For("b"));
        names.Observe([Screen("z", -500, 0), Screen("new", -1000, 0)]);
        Assert.Equal("Display 2", names.For("z")); Assert.Equal("Display 1", names.For("new"));
        Assert.Equal("Display (not connected)", names.For("A"));
        Assert.Equal("Display (not connected)", names.For("unknown"));
    }
    [Fact] public void FriendlyBuiltInAndDuplicateNamesPreserveVendorText()
    {
        var names = new MonitorNames();
        names.Observe([Screen("a", -1920, 0) with { FriendlyName = "  DELL U2720Q  ", StableId = "left" },
            Screen("b", 0, 0, true) with { IsInternal = true },
            Screen("c", 1920, 0) with { FriendlyName = "DELL U2720Q", StableId = "right" }]);
        Assert.Equal("DELL U2720Q (1)", names.For("a"));
        Assert.Equal("Built-in display", names.For("b"));
        Assert.Equal("DELL U2720Q (2)", names.For("c"));
        Assert.Equal("DELL U2720Q (1)", names.For("c", "LEFT"));
        Assert.Equal("Old panel (not connected)", names.For("a", "missing", "Old panel"));
    }
    [Fact] public void ReplugRenumbersCurrentDevicesAndEmptyTopologyClearsAllNames()
    {
        var names = new MonitorNames();
        names.Observe([Screen("a", 0, 0), Screen("b", 1920, 0)]);
        names.Observe([Screen("b", 1920, 0)]);
        Assert.Equal("Display 1", names.For("b"));
        names.Observe([Screen("new-a", 0, 0), Screen("b", 1920, 0)]);
        Assert.Equal("Display 2", names.For("b"));
        names.Observe([]);
        Assert.Equal("Saved (not connected)", names.For("b", savedName: "Saved"));
    }
    [Fact] public void StableIdWinsOverRecycledDeviceAndExactGeometry()
    {
        var left = Screen("device1", 0, 0, true) with { StableId = "physical-left" };
        var right = Screen("device2", 1920, 0) with { StableId = "physical-right" };
        var saved = new SavedWindow { MonitorDevice = left.Device, MonitorStableId = right.StableId,
            MonitorBounds = left.Bounds, MonitorWorkArea = left.WorkArea, Rect = new(100, 100, 500, 400) };
        Assert.Equal(new PixelRect(2020, 100, 500, 400), DisplayMapper.Map(saved, [left, right]));
        saved.MonitorStableId = "";
        Assert.Equal(saved.Rect, DisplayMapper.Map(saved, [left, right]));
    }
    [Fact] public void OldSavedJsonDefaultsMetadataAndNewJsonRoundTrips()
    {
        var old = System.Text.Json.JsonSerializer.Deserialize<SavedWindow>("{\"MonitorDevice\":\"DISPLAY1\"}")!;
        Assert.Equal("", old.MonitorStableId); Assert.Equal("", old.MonitorName);
        old.MonitorStableId = "physical"; old.MonitorName = "Vendor Name";
        var copy = System.Text.Json.JsonSerializer.Deserialize<SavedWindow>(System.Text.Json.JsonSerializer.Serialize(old))!;
        Assert.Equal(old.MonitorStableId, copy.MonitorStableId); Assert.Equal(old.MonitorName, copy.MonitorName);
    }
}
