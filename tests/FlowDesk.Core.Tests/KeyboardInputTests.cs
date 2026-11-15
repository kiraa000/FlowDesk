using FlowDesk.Core.Hotkeys;

namespace FlowDesk.Core.Tests;

public sealed class ShortcutKeyboardStateTests
{
    [Theory]
    [InlineData(0x5B)]
    [InlineData(0x5C)]
    public void OnlyAcceptedGestureArmsWinReleaseAndFreshHoldStartsClean(int key)
    {
        var state = new ShortcutKeyboardState();
        state.Observe(key, true, 0);
        var tap = state.Observe(key, false, 0);
        Assert.True(tap.WinKeyUp);
        Assert.False(tap.GestureAccepted);
        state.Observe(key, true, 0);
        state.AcceptWinGesture();
        var used = state.Observe(key, false, 0);
        Assert.True(used.GestureAccepted);
        Assert.True(used.ResetSearch);
        Assert.False(state.Modifiers.Win);
        Assert.True(used.HoldId > tap.HoldId);
        Assert.False(state.Observe(key, false, 0).GestureAccepted); // duplicate up
        state.Observe(key, true, 0);
        Assert.False(state.Observe(key, false, 0).GestureAccepted);
    }

    [Fact]
    public void KeyRepeatAfterGestureDoesNotDisarmReleaseMaskOrStartANewHold()
    {
        var state = new ShortcutKeyboardState();
        state.Observe(0x5B, true, 0);
        long hold = state.LeftHoldId;
        state.AcceptWinGesture();
        for (int i = 0; i < 5; i++) state.Observe(0x5B, true, 0);
        var up = state.Observe(0x5B, false, 0);
        Assert.True(up.GestureAccepted);
        Assert.Equal(hold, up.HoldId);
        Assert.Equal(5, up.Repeats);
    }

    [Fact]
    public void OwnTagIsExcludedButOtherInjectedInputStillUpdatesModifierState()
    {
        var state = new ShortcutKeyboardState();
        Assert.True(state.Observe(0x5B, true, ShortcutKeyboardState.InjectionTag).OwnInput);
        Assert.False(state.Modifiers.Win);
        state.Observe(0x5B, true, 123); // other app's injection tag, async state irrelevant
        Assert.True(state.Modifiers.Win);
        state.AcceptWinGesture();
        var own = state.Observe(0x5B, false, ShortcutKeyboardState.InjectionTag);
        Assert.True(own.OwnInput);
        Assert.False(own.WinKeyUp);
        Assert.False(own.ResetSearch);
        Assert.True(state.Modifiers.Win);
        Assert.True(state.Observe(0x5B, false, 123).GestureAccepted);
    }

    [Theory]
    [InlineData(0x5B, 0x5C)]
    [InlineData(0x5C, 0x5B)]
    public void OverlappingWinKeysRetainMaskUntilBothReleasedIncludingKeyPressedAfterGesture(int first, int second)
    {
        var state = new ShortcutKeyboardState();
        state.Observe(first, true, 0);
        state.AcceptWinGesture();
        state.Observe(second, true, 0);
        var firstUp = state.Observe(first, false, 0);
        Assert.True(firstUp.GestureAccepted);
        Assert.False(firstUp.ResetSearch);
        Assert.True(state.Modifiers.Win);
        state.Observe(second, true, 0); // repeat can invalidate the earlier mask
        Assert.False(state.Observe(first, false, 0).GestureAccepted); // unmatched repeat up
        var lastUp = state.Observe(second, false, 0);
        Assert.True(lastUp.GestureAccepted);
        Assert.True(lastUp.ResetSearch);
    }

    [Fact]
    public void TracksIndependentLeftRightModifiersAndResetsSearchOnlyAfterLastAltRelease()
    {
        var state = new ShortcutKeyboardState();
        foreach (int key in new[] { 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5 }) state.Observe(key, true, 0);
        Assert.Equal(new ShortcutModifiers(false, true, true, true), state.Modifiers);
        state.Observe(0xA0, false, 0); state.Observe(0xA2, false, 0);
        Assert.False(state.Observe(0xA4, false, 0).ResetSearch);
        Assert.Equal(new ShortcutModifiers(false, true, true, true), state.Modifiers);
        state.Observe(0xA1, false, 0); state.Observe(0xA3, false, 0);
        Assert.True(state.Observe(0xA5, false, 0).ResetSearch);
        Assert.Equal(default, state.Modifiers);
    }

    [Fact]
    public void NormalizesGenericInjectedModifierKeysUsingScanAndExtendedFlags()
    {
        var state = new ShortcutKeyboardState();
        state.Observe(0x10, true, 0, scanCode: 0x36);
        state.Observe(0x11, true, 0, extended: true);
        state.Observe(0x12, true, 0, extended: true);
        Assert.Equal(new ShortcutModifiers(false, true, true, true), state.Modifiers);
        state.Observe(0xA1, false, 0); state.Observe(0xA3, false, 0); state.Observe(0xA5, false, 0);
        Assert.Equal(default, state.Modifiers);
    }

    [Fact]
    public void SeedIncludesKeysHeldWhenHookWasInstalledAndDoesNotPollWhileObserving()
    {
        var state = new ShortcutKeyboardState();
        var sampled = new List<int>();
        state.Seed(key => { sampled.Add(key); return key is 0x5C or 0xA0; });
        Assert.Equal(8, sampled.Count);
        Assert.Equal(new ShortcutModifiers(true, false, true, false), state.Modifiers);
        state.AcceptWinGesture();
        Assert.True(state.Observe(0x5C, false, 0).GestureAccepted);
        Assert.Equal(8, sampled.Count);
    }

    [Fact]
    public void NoWinHeldOrUnrelatedKeysCannotArmARelease()
    {
        var state = new ShortcutKeyboardState();
        state.AcceptWinGesture();
        Assert.Equal(default, state.Observe(0x41, true, 0));
        Assert.Equal(default, state.Observe(0x41, false, 0));
        Assert.False(state.Observe(0x5B, false, 0).GestureAccepted);
    }
}

public sealed class WinKeyReleaseTests
{
    [Theory]
    [InlineData(0x5B)]
    [InlineData(0x5C)]
    public void SuppressOnlyAfterOneCompleteTaggedMaskThenExtendedWinUpBatch(int key)
    {
        var calls = new List<ShortcutKeyStroke[]>();
        var result = WinKeyRelease.Process((ushort)key, true, false, batch => { calls.Add(batch); return (uint)batch.Length; });
        Assert.True(result.Swallow);
        Assert.True(result.MaskInjected);
        Assert.False(result.Failed);
        Assert.Equal(3u, result.Inserted);
        var batch = Assert.Single(calls);
        Assert.Equal(new ShortcutKeyStroke[] { new(0xE8, false), new(0xE8, true), new((ushort)key, true, true) }, batch);
        Assert.All(batch, stroke => Assert.Equal((nuint)ShortcutKeyboardState.InjectionTag, stroke.ExtraInfo));
        Assert.DoesNotContain(batch, stroke => stroke.VirtualKey == key && !stroke.KeyUp);
    }

    [Theory]
    [InlineData(false, false, 0x5B)]
    [InlineData(true, true, 0x5B)]
    [InlineData(true, false, 0x12)]
    [InlineData(true, false, 0x41)]
    public void PlainTapStoppingAndNonWinKeysPassOriginalWithoutAnyInjection(bool accepted, bool stopping, int key)
    {
        var result = WinKeyRelease.Process((ushort)key, accepted, stopping, _ => throw new InvalidOperationException("Must not call sender"));
        Assert.False(result.Swallow);
        Assert.False(result.Attempted);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData(2u)]
    public void ZeroOrPartialInsertionNeverSuppressesPhysicalReleaseAndCleansMaskUp(uint inserted)
    {
        var calls = new List<ShortcutKeyStroke[]>();
        var result = WinKeyRelease.Process(0x5B, true, false, batch =>
        {
            calls.Add(batch);
            return calls.Count == 1 ? inserted : 1;
        });
        Assert.False(result.Swallow); // original up is forwarded, Win cannot be stranded by this hook
        Assert.True(result.Failed);
        Assert.Equal(inserted, result.Inserted);
        Assert.Equal(new ShortcutKeyStroke[] { new(0xE8, true) }, calls[1]);
        Assert.Equal(1u, result.CleanupInserted);
        Assert.DoesNotContain(calls.SelectMany(c => c), key => key.VirtualKey is 0x5B or 0x5C && !key.KeyUp);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SenderAndCleanupExceptionsFailOpenWithNoDeferredWinRelease(bool cleanupAlsoThrows)
    {
        int calls = 0;
        var result = WinKeyRelease.Process(0x5C, true, false, batch =>
        {
            calls++;
            if (calls == 1 || cleanupAlsoThrows) throw new InvalidOperationException();
            return (uint)batch.Length;
        });
        Assert.False(result.Swallow);
        Assert.True(result.Failed);
        Assert.Equal(2, calls);
    }

    [Fact]
    public void StopRacingSuccessfulInjectionCannotAbandonReplacementRelease()
    {
        bool stopping = false;
        ShortcutKeyStroke[]? submitted = null;
        var result = WinKeyRelease.Process(0x5B, true, stopping, batch =>
        {
            submitted = batch;
            stopping = true; // stop/unhook may follow; Win-up is already in the SAME inserted batch
            return 3;
        });
        Assert.True(stopping);
        Assert.True(result.Swallow);
        Assert.Equal(new ShortcutKeyStroke(0x5B, true, true), submitted![^1]);
    }

    [Fact]
    public void ReentrantTaggedReplacementUpCannotRearmMaskOrCorruptNewPhysicalHold()
    {
        var state = new ShortcutKeyboardState();
        state.Observe(0x5B, true, 0);
        state.AcceptWinGesture();
        var release = state.Observe(0x5B, false, 0);
        var result = WinKeyRelease.Process(0x5B, release.GestureAccepted, false, batch =>
        {
            state.Observe(0x5B, true, 0); // a later physical hold before delayed injected-up observation
            foreach (var key in batch)
                Assert.True(state.Observe(key.VirtualKey, !key.KeyUp, key.ExtraInfo).OwnInput);
            return 3;
        });
        Assert.True(result.Swallow);
        Assert.True(state.Modifiers.Win); // injected release belongs to previous hold, not physical state
        Assert.False(state.Observe(0x5B, false, 0).GestureAccepted);
    }
}
