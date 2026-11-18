using FlowDesk.Core.Hotkeys;
using FlowDesk.Core.Palette;

namespace FlowDesk.Core.Tests;

public sealed class PaletteHotkeyInputTests
{
    [Theory]
    [InlineData(true, false, false, true, true)]
    [InlineData(true, false, false, false, false)]
    [InlineData(true, true, false, true, false)]
    [InlineData(true, false, true, true, false)]
    [InlineData(false, false, false, true, false)]
    public void DeactivationCannotDismissAnUnacquiredOrOpeningPalette(bool open, bool opening, bool pending, bool acquired, bool expected) =>
        Assert.Equal(expected, PaletteFocusRules.ShouldHideOnDeactivation(open, opening, pending, acquired));
    private static readonly HotkeyBinding Palette = HotkeyCatalog.Find(HotkeyCatalog.PaletteId)!;
    [Fact] public void OwnsBothTriggerHalvesBeforeAnyFocusChangeAndOnlyActivatesOnce()
    {
        var state = new HotkeyInputOwnership();
        Assert.Equal(new OwnedHotkeyEvent(true, true), state.Observe(0x20, true, 0, Palette));
        Assert.Equal(new OwnedHotkeyEvent(true, false), state.Observe(0x20, true, 1, Palette));
        Assert.Equal(new OwnedHotkeyEvent(true, false), state.Observe(0x20, false, 2));
        Assert.Equal(default, state.Observe(0x20, false, 3));
    }
    [Fact] public void UnregisteredAndUnrelatedKeysAreNotSwallowed()
    {
        var state = new HotkeyInputOwnership();
        Assert.Equal(default, state.Observe(0x20, true, 0));
        state.Observe(0x20, true, 1, Palette);
        Assert.Equal(default, state.Observe(0x41, false, 2));
        Assert.True(state.Observe(0x20, false, 3).Swallow);
    }
    [Fact] public void ReboundClipboardAndRepeatableWindowShortcutUseSameRules()
    {
        var state = new HotkeyInputOwnership();
        var rebound = Palette with { Id = HotkeyCatalog.ClipboardId, VirtualKey = 0x56 };
        Assert.True(state.Observe(0x56, true, 0, rebound).Activate);
        Assert.True(state.Observe(0x56, false, 1).Swallow);
        var repeating = rebound with { Repeat = true };
        Assert.True(state.Observe(0x56, true, 2, repeating).Activate);
        Assert.True(state.Observe(0x56, true, 3, repeating).Activate);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void TimeoutOrResetDropsOldOwnership(bool reset)
    {
        var state = new HotkeyInputOwnership();
        state.Observe(0x20, true, 0, Palette);
        if (reset) state.Reset();
        Assert.Equal(default, state.Observe(0x20, false, HotkeyInputOwnership.TimeoutMilliseconds));
        Assert.False(state.TakeAltMask(0xA4, false, HotkeyInputOwnership.TimeoutMilliseconds));
    }
    [Fact] public void AltIsMaskedOnceWithoutSuppressingItsBalancedRelease()
    {
        var state = new HotkeyInputOwnership();
        state.Observe(0x20, true, 0, Palette);
        Assert.False(state.TakeAltMask(0xA4, true, 1));
        Assert.True(state.TakeAltMask(0xA4, false, 2));
        Assert.False(state.TakeAltMask(0xA4, false, 3));
        Assert.Equal(default, state.Observe(0xA4, false, 3));
    }
    [Fact] public void AcceptedHotkeyStillUsesTransactionalWinReleaseMask()
    {
        var keyboard = new ShortcutKeyboardState();
        keyboard.Observe(0x5B, true, 0);
        keyboard.AcceptWinGesture();
        var up = keyboard.Observe(0x5B, false, 0);
        var result = WinKeyRelease.Process(0x5B, up.GestureAccepted, false, keys =>
        {
            Assert.Equal(new ushort[] { 0xE8, 0xE8, 0x5B }, keys.Select(k => k.VirtualKey));
            return 3;
        });
        Assert.True(result.Swallow);
    }
    [Theory] [InlineData(0x5B)] [InlineData(0x5C)]
    public void LoneWindowsKeyTap(int win)
    {
        var state = new LoneWindowsKey();
        Assert.False(state.ObserveKey(win, true, 0, true));
        Assert.True(state.ObserveKey(win, false, 100, true));
        Assert.False(state.ObserveKey(win, false, 101, true));
    }
    [Theory] [InlineData(500)] [InlineData(1000)] [InlineData(-1)]
    public void LongOrBackwardsHoldDoesNotActivate(long end)
    {
        var state = new LoneWindowsKey();
        state.ObserveKey(0x5B, true, 0, true);
        Assert.False(state.ObserveKey(0x5B, false, end, true));
    }
    [Theory]
    [InlineData(0x4C)] [InlineData(0x45)] [InlineData(0x44)] [InlineData(0x09)]
    [InlineData(0x10)] [InlineData(0x31)] [InlineData(0x12)] [InlineData(0x5C)]
    public void AnyOtherKeyDisqualifiesTheTap(int other)
    {
        var state = new LoneWindowsKey();
        state.ObserveKey(0x5B, true, 0, true);
        state.ObserveKey(other, true, 10, true);
        state.ObserveKey(other, false, 20, true);
        Assert.False(state.ObserveKey(0x5B, false, 100, true));
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void MouseButtonOrWheelDisqualifiesTap(bool wheel)
    {
        var state = new LoneWindowsKey();
        state.ObserveKey(0x5B, true, 0, true);
        state.ObserveMouse(!wheel, false, wheel);
        state.ObserveMouse(false, !wheel, false);
        Assert.False(state.ObserveKey(0x5B, false, 100, true));
    }
    [Fact] public void RepeatDoesNotRestartHoldAndOptionOffOrHeldInputNeverArms()
    {
        var state = new LoneWindowsKey();
        state.ObserveKey(0x5B, true, 0, true);
        state.ObserveKey(0x5B, true, 450, true);
        Assert.False(state.ObserveKey(0x5B, false, 550, true));
        state.ObserveKey(0x5B, true, 600, false);
        Assert.False(state.ObserveKey(0x5B, false, 700, true));
        state.ObserveKey(0x41, true, 800, true);
        state.ObserveKey(0x5B, true, 850, true);
        Assert.False(state.ObserveKey(0x5B, false, 900, true));
        state.Reset();
        state.ObserveKey(0x5B, true, 1000, true, otherInputHeld: true);
        Assert.False(state.ObserveKey(0x5B, false, 1100, true));
    }
    [Fact] public void OptionOffOrPauseAtReleaseAndResetNeverMaskStart()
    {
        var state = new LoneWindowsKey();
        state.ObserveKey(0x5C, true, 0, true);
        Assert.False(state.ObserveKey(0x5C, false, 100, false));
        state.ObserveKey(0x5C, true, 200, true);
        state.Reset();
        Assert.False(state.ObserveKey(0x5C, false, 300, true));
    }
}
