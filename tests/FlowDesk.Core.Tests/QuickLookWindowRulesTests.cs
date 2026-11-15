using FlowDesk.Core.QuickLook;

namespace FlowDesk.Core.Tests;

public sealed class QuickLookWindowRulesTests
{
    [Theory]
    [InlineData(0x04090409, false)] // live regression: ImmIsIME claimed en-US was an IME
    [InlineData(0x08090809, false)] [InlineData(0x040c040c, false)] [InlineData(0, false)]
    [InlineData(0x04040404, true)] [InlineData(0x08040804, true)] // traditional + simplified Chinese
    [InlineData(0x04110411, true)] [InlineData(0x04120412, true)]
    public void OnlyChineseJapaneseKoreanLayoutsBypass(int layout, bool expected) =>
        Assert.Equal(expected, QuickLookWindowRules.BypassSpaceForLayout((nint)layout));

    [Fact] public void ActiveTabWinsEvenWhenBackgroundTabIsFirst()
    {
        ShellTabCandidate[] entries = [new(10, 100), new(20, 200), new(10, 101)];
        Assert.Equal(2, QuickLookWindowRules.SelectTab(10, 101, entries));
        Assert.Equal(0, QuickLookWindowRules.SelectTab(10, 100, entries));
    }
    [Fact] public void UnknownOrChangedActiveTabNeverFallsBackToBackground()
    {
        ShellTabCandidate[] entries = [new(10, 100), new(10, 0)];
        Assert.Equal(-1, QuickLookWindowRules.SelectTab(10, 101, entries));
        Assert.Equal(-1, QuickLookWindowRules.SelectTab(10, -1, entries));
        Assert.Equal(-1, QuickLookWindowRules.SelectTab(20, 100, entries));
    }
    [Fact] public void LegacyExplorerNeedsExactlyOneOwnerMatch()
    {
        Assert.Equal(1, QuickLookWindowRules.SelectTab(10, 0, [new(20, 0), new(10, 0)]));
        Assert.Equal(-1, QuickLookWindowRules.SelectTab(10, 0, [new(10, 0), new(10, 0)]));
        Assert.Equal(-1, QuickLookWindowRules.SelectTab(10, 0, []));
        Assert.Equal(-1, QuickLookWindowRules.SelectTab(10, 100, [new(10, 100), new(10, 100)]));
    }
    [Fact] public void ZOrderRaisesWithoutActivationAndHideClearsTopmostWithoutReshowing()
    {
        var show = QuickLookWindowRules.ZOrder(true); var hide = QuickLookWindowRules.ZOrder(false);
        Assert.Equal((nint)(-1), show.InsertAfter); Assert.Equal((nint)(-2), hide.InsertAfter);
        Assert.Equal(0x10u, show.Flags & 0x10u); Assert.Equal(0x10u, hide.Flags & 0x10u);
        Assert.Equal(3u, show.Flags & 3u); Assert.Equal(3u, hide.Flags & 3u);
        Assert.Equal(0x40u, show.Flags & 0x40u); Assert.Equal(0u, hide.Flags & 0x40u);
    }
}
