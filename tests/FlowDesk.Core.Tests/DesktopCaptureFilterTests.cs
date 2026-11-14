using FlowDesk.Core.Capture;
using FlowDesk.Core.Desktops;
using FlowDesk.Core.Windows;

namespace FlowDesk.Core.Tests;

/// <summary>
/// Cloaked windows are only capturable when the cloak says "another virtual desktop" (DWM_CLOAKED_SHELL)
/// and the desktop API confirms a known desktop that is not the current one. Shell UI, app cloaks and
/// inherited cloaks must stay out.
/// </summary>
public class DesktopCaptureFilterTests
{
    private static readonly Guid DesktopB = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly WindowDesktopInfo OnB = new(1, DesktopB, false);

    [Theory]
    [InlineData(0, 1, false, true)]   // not cloaked: normal window
    [InlineData(2, 1, false, true)]   // shell cloak + valid index on another desktop
    [InlineData(2, 0, false, true)]   // shell cloak + valid index 0 on another desktop
    [InlineData(2, 1, true, false)]   // shell cloak but the window IS on the current desktop
    [InlineData(2, -1, false, false)] // shell cloak + no known desktop
    [InlineData(-1, 1, false, false)] // unknown sentinel: never assume an uncloaked/local window
    [InlineData(1, 1, false, false)]  // app cloak (not a desktop window)
    [InlineData(4, 1, false, false)]  // inherited cloak
    [InlineData(3, 1, false, false)]  // app + shell: mixed reasons are not trusted
    [InlineData(6, 1, false, false)]  // shell + inherited: mixed
    [InlineData(7, 1, false, false)]  // all cloak flags
    public void Cloak_reason_table(int reason, int index, bool isCurrent, bool expected)
    {
        var info = new WindowDesktopInfo(index, null, isCurrent);

        Assert.Equal(expected, DesktopCaptureFilter.AllowCloaked(reason, info));
    }

    [Fact]
    public void Guid_only_other_desktop_is_allowable()
    {
        Assert.True(DesktopCaptureFilter.AllowCloaked(2, new WindowDesktopInfo(null, DesktopB, false)));
    }

    [Fact]
    public void Empty_guid_is_not_a_known_desktop()
    {
        Assert.False(DesktopCaptureFilter.AllowCloaked(2, new WindowDesktopInfo(null, Guid.Empty, false)));
    }

    [Fact]
    public void Other_desktops_can_be_excluded_entirely()
    {
        Assert.False(DesktopCaptureFilter.AllowCloaked(2, OnB, includeOtherDesktops: false));
        Assert.True(DesktopCaptureFilter.AllowCloaked(0, new WindowDesktopInfo(0, null, true), includeOtherDesktops: false));
    }

    [Fact]
    public void WindowDesktopInfo_reads_index_id_and_current_from_the_backend()
    {
        var desktops = new FakeVirtualDesktops();
        const long handle = 42;
        desktops.DesktopNumbers[handle] = 2;
        desktops.DesktopIds[handle] = DesktopB;
        desktops.OnCurrent[handle] = false;

        var info = WindowDesktopInfo.Read(desktops, handle);

        Assert.Equal(2, info.Index);
        Assert.Equal(DesktopB, info.Id);
        Assert.False(info.IsCurrent);
    }

    [Theory]
    [InlineData(0, null, true)]
    [InlineData(-1, null, false)]
    [InlineData(null, "aaaaaaaa-0000-0000-0000-000000000001", true)]
    [InlineData(null, "00000000-0000-0000-0000-000000000000", false)]
    [InlineData(-1, "00000000-0000-0000-0000-000000000000", false)]
    [InlineData(null, null, false)]
    public void HasIdentity_requires_a_positive_index_or_a_nonempty_guid(int? index, string? id, bool expected)
    {
        var info = new WindowDesktopInfo(index, id is null ? null : Guid.Parse(id), false);

        Assert.Equal(expected, info.HasIdentity);
    }

    [Theory]
    [InlineData("aaaaaaaa-0000-0000-0000-000000000001", 0, "aaaaaaaa-0000-0000-0000-000000000001", 0, false)]
    [InlineData("aaaaaaaa-0000-0000-0000-000000000001", 0, "bbbbbbbb-0000-0000-0000-000000000002", 0, true)]
    [InlineData(null, 0, null, 1, true)]
    [InlineData(null, 0, null, 0, false)]
    [InlineData(null, -1, null, 0, false)]
    [InlineData(null, null, null, null, false)]
    [InlineData("aaaaaaaa-0000-0000-0000-000000000001", 0, null, 0, false)]
    [InlineData("00000000-0000-0000-0000-000000000000", 0, "aaaaaaaa-0000-0000-0000-000000000001", 0, false)]
    [InlineData("aaaaaaaa-0000-0000-0000-000000000001", 0, "aaaaaaaa-0000-0000-0000-000000000001", 1, false)] // matching GUID wins over reordered index
    public void ConflictsWith_needs_two_positive_comparable_identities(
        string? id, int? index, string? otherId, int? otherIndex, bool expected)
    {
        var info = new WindowDesktopInfo(index, id is null ? null : Guid.Parse(id), null);
        var other = new WindowDesktopInfo(otherIndex, otherId is null ? null : Guid.Parse(otherId), null);

        Assert.Equal(expected, info.ConflictsWith(other));
    }

    [Fact]
    public void WindowDesktopInfo_normalizes_invalid_index_and_empty_guid()
    {
        var desktops = new FakeVirtualDesktops();
        const long handle = 43;
        desktops.DesktopNumbers[handle] = -1;
        desktops.DesktopIds[handle] = Guid.Empty;
        // The hotkey fake defaults unknown handles to "on current" for backward compatibility;
        // this case needs an explicit unknown answer so IsCurrent stays null.
        desktops.OnCurrent[handle] = null;

        var info = WindowDesktopInfo.Read(desktops, handle);

        Assert.Null(info.Index);
        Assert.Null(info.Id);
        Assert.Null(info.IsCurrent);
    }

    [Theory]
    [InlineData(0, new int[0], 0)]                    // no candidates
    [InlineData(0, new[] { 0, -1 }, 0)]               // invalid PIDs ignored
    [InlineData(0, new[] { 42, 42 }, 42)]             // duplicate of one positive PID
    [InlineData(0, new[] { 42, 43 }, 0)]              // ambiguous: never guess the first
    [InlineData(43, new[] { 42, 43 }, 43)]            // cached known PID wins among two
    [InlineData(0, new[] { 42 }, 42)]                 // absent cache + one distinct candidate
    public void SelectDetachedProcess_accepts_only_an_unambiguous_app_pid(int known, int[] candidates, int expected)
    {
        Assert.Equal(expected, Win32WindowSystem.SelectDetachedProcess(known, candidates));
    }
}
