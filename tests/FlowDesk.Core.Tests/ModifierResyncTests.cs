using FlowDesk.Core.Hotkeys;

namespace FlowDesk.Core.Tests;

public sealed class ModifierResyncTests
{
    private static WheelGesture Route(ShortcutKeyboardState state)
    {
        var m = state.Modifiers;
        return WheelGestures.Route(m.Win, m.Alt, m.Shift, m.Control);
    }

    [Theory]
    [InlineData(0x5B)]
    [InlineData(0x5C)]
    public void LostWinUpIsClearedBeforeRoutingAndLateUpCannotInjectReplacement(int win)
    {
        long now = 0;
        var state = new ShortcutKeyboardState(() => now);
        state.Observe(win, true, 0);
        state.AcceptWinGesture();
        now = ShortcutKeyboardState.StaleDownGraceMilliseconds;
        Assert.True(state.ReconcileStale(_ => false));
        Assert.Equal(WheelGesture.None, Route(state));
        var lateUp = state.Observe(win, false, 0);
        Assert.False(lateUp.GestureAccepted);
        var replacement = WinKeyRelease.Process((ushort)win, lateUp.GestureAccepted, false,
            _ => throw new InvalidOperationException("A stale clear must never inject Win-up"));
        Assert.False(replacement.Attempted);
        Assert.False(replacement.Swallow);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(999)]
    public void FreshHookOnlyWinIsRetainedEvenWhenAsyncStateIsUp(int age)
    {
        long now = 100;
        var state = new ShortcutKeyboardState(() => now);
        state.Observe(0x5B, true, 123); // other app's injected event need not appear in async state
        now += age;
        int samples = 0;
        Assert.False(state.ReconcileStale(_ => { samples++; return false; }));
        Assert.Equal(0, samples); // do not even sample until the grace boundary
        Assert.Equal(WheelGesture.Desktop, Route(state));
        state.AcceptWinGesture();
        Assert.True(state.Observe(0x5B, false, 0).GestureAccepted); // proven release path remains eligible
    }

    [Theory]
    [InlineData(1000)]
    [InlineData(1001)]
    [InlineData(60_000)]
    public void AsyncUpClearsAtAndBeyondExactGraceBoundary(int age)
    {
        long now = 0;
        var state = new ShortcutKeyboardState(() => now);
        state.Observe(0x5B, true, 0);
        now = age;
        Assert.True(state.ReconcileStale(_ => false));
        Assert.False(state.Modifiers.Win);
    }

    [Fact]
    public void RepeatsDoNotExtendTheOriginalDownGracePeriod()
    {
        long now = 0;
        var state = new ShortcutKeyboardState(() => now);
        state.Observe(0x5B, true, 0);
        state.AcceptWinGesture();
        now = 999;
        state.Observe(0x5B, true, 0);
        now = 1000;
        Assert.True(state.ReconcileStale(_ => false));
        Assert.Equal(WheelGesture.None, Route(state));
        Assert.False(state.Observe(0x5B, false, 0).GestureAccepted);
    }

    [Fact]
    public void LongPhysicalHoldConfirmedByAsyncStateKeepsItsReleaseArm()
    {
        long now = 0;
        var state = new ShortcutKeyboardState(() => now);
        state.Observe(0x5B, true, 0);
        state.AcceptWinGesture();
        for (int i = 0; i < 214; i++)
        {
            now += 100;
            state.Observe(0x5B, true, 0);
            Assert.False(state.ReconcileStale(_ => true));
        }
        var up = state.Observe(0x5B, false, 0);
        Assert.True(up.GestureAccepted);
        Assert.Equal(214, up.Repeats);
        var replacement = WinKeyRelease.Process(0x5B, up.GestureAccepted, false, batch => (uint)batch.Length);
        Assert.True(replacement.Swallow);
        Assert.Equal(3u, replacement.Inserted);
    }

    [Fact]
    public void LostAltShiftUpsCannotKeepResizingPlainWheel()
    {
        long now = 0;
        var state = new ShortcutKeyboardState(() => now);
        state.Observe(0xA4, true, 0);
        state.Observe(0xA0, true, 0);
        Assert.Equal(WheelGesture.Resize, Route(state));
        now = 1000;
        Assert.True(state.ReconcileStale(_ => false));
        Assert.Equal(WheelGesture.None, Route(state));
        Assert.Equal(default, state.Modifiers);
    }

    [Fact]
    public void ReconciliationClearsEverySideButDoesNotPromoteUnobservedAsyncDowns()
    {
        long now = 0;
        var state = new ShortcutKeyboardState(() => now);
        foreach (int key in new[] { 0x5B, 0x5C, 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5 }) state.Observe(key, true, 0);
        now = 1000;
        var sampled = new List<int>();
        Assert.True(state.ReconcileStale(key => { sampled.Add(key); return false; }));
        Assert.Equal(8, sampled.Count);
        Assert.Equal(default, state.Modifiers);
        Assert.False(state.ReconcileStale(_ => throw new InvalidOperationException("No tracked downs to sample")));
        Assert.Equal(WheelGesture.None, Route(state));
    }

    [Theory]
    [InlineData(1u)] // console connect
    [InlineData(2u)] // console disconnect
    [InlineData(3u)] // remote connect
    [InlineData(4u)] // remote disconnect
    [InlineData(5u)] // logon
    [InlineData(6u)] // logoff
    [InlineData(7u)] // lock
    [InlineData(8u)] // unlock
    [InlineData(15u)] // user's desktop ready
    public void SessionBoundaryReseedsLostModifiersAndDropsOldReleaseArm(uint reason)
    {
        var state = new ShortcutKeyboardState(() => 0);
        foreach (int key in new[] { 0x5B, 0xA0, 0xA2, 0xA4 }) state.Observe(key, true, 0);
        state.AcceptWinGesture();
        int samples = 0;
        Assert.True(state.ReseedForSessionChange(reason, _ => { samples++; return false; }));
        Assert.Equal(8, samples);
        Assert.Equal(default, state.Modifiers);
        Assert.Equal(WheelGesture.None, Route(state));
        Assert.False(state.Observe(0x5B, false, 0).GestureAccepted);
    }

    [Fact]
    public void UnlockStillHeldWinGetsFreshTimestampButNoOldGestureArm()
    {
        long now = 0;
        var state = new ShortcutKeyboardState(() => now);
        state.Observe(0x5B, true, 0);
        state.AcceptWinGesture();
        long oldHold = state.LeftHoldId;
        now = 60_000;
        Assert.True(state.ReseedForSessionChange(8, key => key == 0x5B));
        Assert.True(state.LeftHoldId > oldHold);
        now += 999;
        Assert.False(state.ReconcileStale(_ => false));
        Assert.True(state.Modifiers.Win);
        Assert.False(state.Observe(0x5B, false, 0).GestureAccepted);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(9u)]
    [InlineData(10u)]
    [InlineData(11u)]
    public void UnrelatedSessionReasonsDoNotReseedOrDisarmValidHold(uint reason)
    {
        var state = new ShortcutKeyboardState(() => 0);
        state.Observe(0x5B, true, 0);
        state.AcceptWinGesture();
        Assert.False(state.ReseedForSessionChange(reason, _ => throw new InvalidOperationException("Must not sample")));
        Assert.True(state.Observe(0x5B, false, 0).GestureAccepted);
    }

    [Fact]
    public void StaleClearDoesNotDisarmOtherFreshWinButCannotReplaceClearedKeysUp()
    {
        long now = 0;
        var state = new ShortcutKeyboardState(() => now);
        state.Observe(0x5B, true, 0);
        state.AcceptWinGesture();
        now = 900;
        state.Observe(0x5C, true, 0);
        now = 1000;
        Assert.True(state.ReconcileStale(_ => false));
        Assert.True(state.Modifiers.Win);
        Assert.False(state.Observe(0x5B, false, 0).GestureAccepted);
        Assert.True(state.Observe(0x5C, false, 0).GestureAccepted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SamplerFailureCannotPartiallyReseedOrReconcile(bool reseed)
    {
        long now = 0;
        var state = new ShortcutKeyboardState(() => now);
        state.Observe(0x5B, true, 0);
        state.Observe(0x5C, true, 0);
        state.AcceptWinGesture();
        now = 1000;
        int samples = 0;
        bool FailingSampler(int _) { if (++samples == 2) throw new InvalidOperationException(); return false; }
        Assert.Throws<InvalidOperationException>(() =>
        {
            if (reseed) state.Seed(FailingSampler);
            else state.ReconcileStale(FailingSampler);
        });
        Assert.True(state.Modifiers.Win);
        Assert.True(state.Observe(0x5B, false, 0).GestureAccepted);
        Assert.True(state.Observe(0x5C, false, 0).GestureAccepted);
    }

    [Fact]
    public void NewPressAfterStaleClearStartsFreshAndDoesNotInheritOldGesture()
    {
        long now = 0;
        var state = new ShortcutKeyboardState(() => now);
        state.Observe(0x5B, true, 0);
        state.AcceptWinGesture();
        now = 1000;
        state.ReconcileStale(_ => false);
        now = 1001;
        state.Observe(0x5B, true, 0);
        now = 2000;
        Assert.False(state.ReconcileStale(_ => false));
        Assert.Equal(WheelGesture.Desktop, Route(state));
        Assert.False(state.Observe(0x5B, false, 0).GestureAccepted);
    }
}
