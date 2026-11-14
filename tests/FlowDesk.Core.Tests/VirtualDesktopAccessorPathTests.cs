using FlowDesk.Core.Desktops;

namespace FlowDesk.Core.Tests;

public class VirtualDesktopAccessorPathTests
{
    [Fact]
    public void User_chosen_dll_is_tried_before_the_bundled_copy()
    {
        var paths = VirtualDesktopAccessor.CandidatePaths(@"X:\app", @"X:\data");

        Assert.Equal(Path.Combine(@"X:\data", "native", VirtualDesktopAccessor.DllName), paths[0]);
        Assert.Equal(Path.Combine(@"X:\app", VirtualDesktopAccessor.DllName), paths[1]);
    }
}
