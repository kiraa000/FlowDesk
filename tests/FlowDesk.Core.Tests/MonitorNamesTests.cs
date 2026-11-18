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
    [Fact] public void TiesUseTopThenDeviceAndLabelsSurviveReorderAndReconnect()
    {
        var names = new MonitorNames();
        names.Observe([Screen("z", 0, 0), Screen("b", 0, -100), Screen("a", 0, -100)]);
        Assert.Equal("Display 1", names.For("a")); Assert.Equal("Display 2", names.For("b"));
        names.Observe([Screen("z", -500, 0), Screen("new", -1000, 0)]);
        Assert.Equal("Display 3", names.For("z")); Assert.Equal("Display 4", names.For("new"));
        Assert.Equal("Display 1", names.For("A"));
        Assert.Equal("Display (not connected)", names.For("unknown"));
    }
}
