using FlowDesk.Probe;

namespace FlowDesk.Core.Tests;

/// <summary>
/// Desktop-free checks for the Probe uwp-restore frame selection: exact packaged-app pid, one frame only,
/// never ApplicationFrameHost's own pid and never a title/name guess.
/// </summary>
public class UwpRestorePlanTests
{
    private const int AppPid = 7164;
    private const int FramePid = 6540;

    private static FrameChild Frame(long frame, int childPid, int framePid = FramePid, string childClass = "Windows.UI.Core.CoreWindow")
        => new(frame, framePid, frame + 1, childPid, childClass);

    [Fact]
    public void Exactly_one_frame_hosting_the_exact_child_pid_is_accepted()
    {
        var candidates = new[]
        {
            Frame(0x1000, AppPid),
            Frame(0x2000, 9999),
            Frame(0x3000, 8888),
        };

        Assert.True(UwpRestorePlan.TryChoose(AppPid, candidates, out var choice, out var error));
        Assert.Null(error);
        Assert.Equal(0x1000, choice.Frame);
        Assert.Equal(FramePid, choice.FramePid);
        Assert.Equal(AppPid, choice.ChildPid);
        Assert.Equal("Windows.UI.Core.CoreWindow", choice.ChildClass);
    }

    [Fact]
    public void Zero_hosting_frames_is_refused_instead_of_guessing()
    {
        var candidates = new[] { Frame(0x1000, 9999), Frame(0x2000, 8888) };

        Assert.False(UwpRestorePlan.TryChoose(AppPid, candidates, out _, out var error));
        Assert.Contains("no ApplicationFrameWindow", error);
    }

    [Fact]
    public void More_than_one_hosting_frame_is_refused_as_ambiguous()
    {
        var candidates = new[] { Frame(0x1000, AppPid), Frame(0x2000, AppPid) };

        Assert.False(UwpRestorePlan.TryChoose(AppPid, candidates, out _, out var error));
        Assert.Contains("2 frames", error);
    }

    [Fact]
    public void A_frame_pid_matching_the_app_pid_is_not_a_hosted_child()
    {
        var host = new FrameChild(0x1000, AppPid, 0x1001, AppPid, "ApplicationFrameWindow");

        Assert.False(UwpRestorePlan.TryChoose(AppPid, [host], out _, out var error));
        Assert.Contains("no ApplicationFrameWindow", error);
    }

    [Fact]
    public void Multiple_child_boundaries_on_the_same_frame_are_one_frame_choice()
    {
        var candidates = new[] { Frame(0x1000, AppPid), Frame(0x1000, AppPid) };

        Assert.True(UwpRestorePlan.TryChoose(AppPid, candidates, out var choice, out _));
        Assert.Equal(0x1000, choice.Frame);
        Assert.Equal(AppPid, choice.ChildPid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Non_positive_app_pid_is_refused(int pid)
    {
        Assert.False(UwpRestorePlan.TryChoose(pid, [Frame(0x1000, AppPid)], out _, out var error));
        Assert.Contains("positive integer", error);
    }
}
