using FlowDesk.Core.Hotkeys;

namespace FlowDesk.Core.Tests;

public sealed class WheelDeltaTests
{
    [Theory]
    [InlineData(false, 120)]
    [InlineData(false, -120)]
    [InlineData(true, 120)]
    [InlineData(true, -120)]
    public void Mouse_notch_fires_immediately_once(bool move, int delta)
    {
        var state = new WheelGestures();
        var first = state.Accept(true, false, move, false, 0, delta);
        Assert.True(first.Accepted);
        Assert.True(first.MaskMenu);
        Assert.Equal(move ? WheelGesture.MoveDesktop : WheelGesture.Desktop, first.Gesture);
        Assert.False(state.Accept(true, false, move, false, 1, 0).Accepted);
    }

    [Theory]
    [InlineData(false, 10)]
    [InlineData(false, -10)]
    [InlineData(true, 10)]
    [InlineData(true, -10)]
    public void Twelve_small_deltas_fire_once_and_ten_fire_none(bool move, int delta)
    {
        var state = new WheelGestures();
        for (int i = 0; i < 12; i++)
        {
            var decision = state.Accept(true, false, move, false, i * 20, delta);
            Assert.Equal(i == 11, decision.Accepted);
            Assert.Equal(i == 0, decision.MaskMenu);
            Assert.NotEqual(WheelGesture.None, decision.Gesture); // partials are swallowed by the hook
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void Direction_change_discards_previous_partial(int direction)
    {
        var state = new WheelGestures();
        Assert.False(state.Accept(true, false, false, false, 0, 100 * direction).Accepted);
        Assert.False(state.Accept(true, false, false, false, 10, -20 * direction).Accepted);
        Assert.True(state.Accept(true, false, false, false, 20, -100 * direction).Accepted);
    }

    [Theory]
    [InlineData(300)]
    [InlineData(301)]
    [InlineData(-1)] // monotonic-clock discontinuity cannot carry old input forward
    public void Idle_or_clock_reset_discards_previous_partial(long nextTime)
    {
        var state = new WheelGestures();
        Assert.False(state.Accept(true, false, false, false, 0, 100).Accepted);
        Assert.False(state.Accept(true, false, false, false, nextTime, 20).Accepted);
        Assert.True(state.Accept(true, false, false, false, nextTime + 1, 100).Accepted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Win_release_or_full_reset_discards_partial_and_rearms_mask(bool reset)
    {
        var state = new WheelGestures();
        Assert.True(state.Accept(true, false, false, false, 0, 100).MaskMenu);
        if (reset) state.Reset(); else state.ObserveWin(false);
        var first = state.Accept(true, false, false, false, 1, 20);
        Assert.False(first.Accepted);
        Assert.True(first.MaskMenu);
        Assert.True(state.Accept(true, false, false, false, 2, 100).Accepted);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void Accepted_notch_keeps_signed_remainder(int direction)
    {
        var state = new WheelGestures();
        Assert.True(state.Accept(true, false, false, false, 0, 180 * direction).Accepted);
        Assert.True(state.Accept(true, false, false, false, 123, 60 * direction).Accepted);
        Assert.False(state.Accept(true, false, false, false, 246, direction).Accepted);
    }

    [Fact]
    public void Accumulated_input_does_not_bypass_cooldown_and_needs_an_event_to_fire()
    {
        var state = new WheelGestures();
        Assert.True(state.Accept(true, false, false, false, 0, 120).Accepted);
        Assert.False(state.Accept(true, false, false, false, 10, 60).Accepted);
        Assert.False(state.Accept(true, false, false, false, 20, 60).Accepted);
        Assert.True(state.Accept(true, false, false, false, 123, 1).Accepted);
        Assert.False(state.Accept(true, false, false, false, 246, 118).Accepted);
        Assert.True(state.Accept(true, false, false, false, 247, 1).Accepted);
    }

    [Fact]
    public void Desktop_and_move_desktop_do_not_share_partial_input()
    {
        var state = new WheelGestures();
        Assert.False(state.Accept(true, false, false, false, 0, 100).Accepted);
        Assert.False(state.Accept(true, false, true, false, 10, 20).Accepted);
        Assert.True(state.Accept(true, false, true, false, 20, 100).Accepted);
        Assert.True(state.Accept(true, false, false, false, 30, 20).Accepted);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Resize_and_search_keep_their_existing_event_based_behavior(bool search)
    {
        var state = new WheelGestures();
        var decision = state.Accept(search, true, !search, false, 0, 10);
        Assert.Equal(search ? WheelGesture.Search : WheelGesture.Resize, decision.Gesture);
        Assert.True(decision.Accepted);
        Assert.Equal(search, decision.MaskMenu);
    }

    [Theory]
    [InlineData(0x5B, 10)]
    [InlineData(0x5C, -10)]
    [InlineData(0x5B, 120)]
    public void Both_partial_and_full_swipes_mask_Start_on_Win_release(int winKey, int delta)
    {
        var keyboard = new ShortcutKeyboardState();
        var wheel = new WheelGestures();
        keyboard.Observe(winKey, true, 0);
        var decision = wheel.Accept(true, false, false, false, 0, delta);
        Assert.NotEqual(WheelGesture.None, decision.Gesture);
        Assert.Equal(Math.Abs(delta) >= 120, decision.Accepted);
        Assert.True(decision.MaskMenu);
        keyboard.AcceptWinGesture(); // hook arms release protection for every swallowed Win wheel event
        var release = keyboard.Observe(winKey, false, 0);
        Assert.True(release.GestureAccepted);
        var sent = new List<ShortcutKeyStroke>();
        var result = WinKeyRelease.Process((ushort)winKey, release.GestureAccepted, false,
            keys => { sent.AddRange(keys); return (uint)keys.Length; });
        Assert.True(result.MaskInjected);
        Assert.True(result.Swallow);
        Assert.Equal(new ushort[] { 0xE8, 0xE8, (ushort)winKey }, sent.Select(k => k.VirtualKey));
        Assert.True(sent[^1].KeyUp);
    }
}
