using FlowDesk.Core.HotCorners;
using FlowDesk.Core.Hotkeys;

namespace FlowDesk.Core.Tests;

public sealed class HotCornerTests
{
    private static readonly CornerBounds[] Single = [new(0, 0, 1920, 1080)];
    private static readonly HotCornerConfiguration On = new(true);
    private static readonly CornerPoint Origin = new(0, 0);
    private static HotCornerTrigger Armed()
    {
        var trigger = new HotCornerTrigger(); trigger.Tick(new(100, 100), 0, 0, Single, On, null); return trigger;
    }
    [Fact] public void AllSingleMonitorCornersAreOuterFullBoundsNotWorkArea()
    {
        Assert.Equal(4, HotCornerGeometry.Corners(Single[0]).Count());
        Assert.All(HotCornerGeometry.Corners(Single[0]), c => Assert.True(HotCornerGeometry.Outer(c, Single)));
        Assert.NotNull(HotCornerGeometry.At(new(1919, 1079), Single));
        Assert.Null(HotCornerGeometry.At(new(1919, 1039), Single));
        Assert.Null(HotCornerGeometry.At(new(1920, 1080), Single));
    }
    [Theory]
    [InlineData(-1, 0, false)] [InlineData(0, 0, false)] [InlineData(-2560, 0, true)]
    [InlineData(2559, 0, true)] [InlineData(-1, 1079, false)] [InlineData(0, 1079, false)]
    public void SideBySideNegativeCoordinatesRejectSeamCorners(int x, int y, bool outer)
    {
        CornerBounds[] layout = [new(-2560, 0, 0, 1080), new(0, 0, 2560, 1080)];
        Assert.Equal(outer, HotCornerGeometry.Outer(HotCornerGeometry.At(new(x, y), layout)!.Value, layout));
    }
    [Theory]
    [InlineData(-1, 0, false)] [InlineData(-1, 1439, true)] [InlineData(0, 1079, false)]
    public void UnequalHeightsUseActualAdjacentPixels(int x, int y, bool outer)
    {
        CornerBounds[] layout = [new(-2560, 0, 0, 1440), new(0, 0, 2560, 1080)];
        Assert.Equal(outer, HotCornerGeometry.Outer(HotCornerGeometry.At(new(x, y), layout)!.Value, layout));
    }
    [Theory]
    [InlineData(-1, 0, true)] [InlineData(0, 300, false)] [InlineData(-1, 1439, true)] [InlineData(0, 1379, false)]
    public void OffsetHeightsDoNotTreatWholeEdgeAsSeam(int x, int y, bool outer)
    {
        CornerBounds[] layout = [new(-2560, 0, 0, 1440), new(0, 300, 2560, 1380)];
        Assert.Equal(outer, HotCornerGeometry.Outer(HotCornerGeometry.At(new(x, y), layout)!.Value, layout));
    }
    [Fact] public void VerticalStackRejectsBothSidesOfSharedEdge()
    {
        CornerBounds[] layout = [new(0, -1080, 1920, 0), new(0, 0, 1920, 1080)];
        foreach (var point in new CornerPoint[] { new(0, -1), new(1919, -1), new(0, 0), new(1919, 0) })
            Assert.False(HotCornerGeometry.Outer(HotCornerGeometry.At(point, layout)!.Value, layout));
        Assert.True(HotCornerGeometry.Outer(HotCornerGeometry.At(new(0, -1080), layout)!.Value, layout));
    }
    [Fact] public void DwellFiresOnlyAfter250MillisecondsAndNeverRepeatsAtRest()
    {
        var trigger = Armed();
        Assert.Contains("enter", trigger.Tick(Origin, 1, 100, Single, On, null).Diagnostic!);
        Assert.Null(trigger.Tick(Origin, 1, 349, Single, On, null).Fired);
        var fired = trigger.Tick(Origin, 1, 350, Single, On, null);
        Assert.Equal(HotCornerAction.TaskView, fired.Action); Assert.NotNull(fired.Fired);
        Assert.Null(trigger.Tick(Origin, 1, 100000, Single, On, null).Fired);
    }
    [Fact] public void LeaveEarlyCancelsAndReturningStartsNewDwell()
    {
        var trigger = Armed(); trigger.Tick(Origin, 1, 100, Single, On, null);
        Assert.Contains("left early", trigger.Tick(new(1, 0), 2, 200, Single, On, null).Diagnostic!);
        trigger.Tick(Origin, 3, 250, Single, On, null);
        Assert.Null(trigger.Tick(Origin, 3, 499, Single, On, null).Fired);
        Assert.NotNull(trigger.Tick(Origin, 3, 500, Single, On, null).Fired);
    }
    [Fact] public void RearmRequiresLeaving24PixelBuffer()
    {
        var trigger = Armed(); trigger.Tick(Origin, 1, 1, Single, On, null); trigger.Tick(Origin, 1, 251, Single, On, null);
        trigger.Tick(new(24, 0), 2, 300, Single, On, null);
        Assert.Null(trigger.Tick(Origin, 3, 1000, Single, On, null).Fired);
        trigger.Tick(new(25, 0), 4, 1100, Single, On, null);
        trigger.Tick(Origin, 5, 1200, Single, On, null);
        Assert.NotNull(trigger.Tick(Origin, 5, 1450, Single, On, null).Fired);
    }
    [Theory] [InlineData("button held")] [InlineData("full-screen window")] [InlineData("locked or secure desktop")]
    public void BlockedDwellCannotFireWhenBlockEndsUntilRearm(string reason)
    {
        var trigger = Armed(); trigger.Tick(Origin, 1, 1, Single, On, null);
        Assert.Contains(reason, trigger.Tick(Origin, 1, 200, Single, On, reason).Diagnostic!);
        Assert.Null(trigger.Tick(Origin, 1, 1000, Single, On, null).Fired);
        trigger.Tick(new(25, 0), 2, 1100, Single, On, null); trigger.Tick(Origin, 3, 1200, Single, On, null);
        Assert.NotNull(trigger.Tick(Origin, 3, 1450, Single, On, null).Fired);
    }
    [Fact] public void TransientMoveOrClickBetweenPollsResetsContinuousDwell()
    {
        var trigger = Armed(); trigger.Tick(Origin, 1, 100, Single, On, null);
        trigger.Tick(Origin, 3, 200, Single, On, null);
        Assert.Null(trigger.Tick(Origin, 3, 449, Single, On, null).Fired);
        Assert.NotNull(trigger.Tick(Origin, 3, 450, Single, On, null).Fired);
    }
    [Fact] public void EnableOrContextResetAtCornerDoesNotFireUntilNeutral()
    {
        var trigger = new HotCornerTrigger(); trigger.Tick(Origin, 1, 0, Single, On, null);
        Assert.Null(trigger.Tick(Origin, 1, 10000, Single, On, null).Fired);
        trigger.Tick(new(100, 100), 2, 10100, Single, On, null); trigger.Tick(Origin, 3, 10200, Single, On, null);
        trigger.Reset(); Assert.Null(trigger.Tick(Origin, 3, 11000, Single, On, null).Fired);
    }
    [Fact] public void DisabledNoneAndInnerCornersDoNotFire()
    {
        var trigger = Armed(); Assert.Null(trigger.Tick(Origin, 1, 1, Single, new(false), null).Fired);
        trigger = Armed(); trigger.Tick(new(1919, 0), 1, 1, Single, On, null);
        Assert.Null(trigger.Tick(new(1919, 0), 1, 1000, Single, On, null).Fired);
        CornerBounds[] layout = [new(-2560, 0, 0, 1080), new(0, 0, 2560, 1080)];
        trigger = Armed(); Assert.Contains("inner corner", trigger.Tick(Origin, 1, 1, layout, On, null).Diagnostic!);
        Assert.Equal(HotCornerAction.None, new HotCornerConfiguration(true, (HotCornerAction)999).For(HotCorner.TopLeft));
        Assert.Equal(HotCornerAction.None, On.For((HotCorner)999));
    }
    [Theory] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(0)] [InlineData(8)]
    public void NotificationUnsafeOrUnknownStateBlocks(int state) => Assert.NotNull(HotCornerGate.BlockReason(true, true, false, state, false, false, false, false));
    [Fact] public void NativeGateRequiresKnownSafeInputAndAllowsShellDesktop()
    {
        Assert.Null(HotCornerGate.BlockReason(true, true, false, 5, true, true, false, false));
        Assert.NotNull(HotCornerGate.BlockReason(true, true, false, 5, true, false, false, false));
        Assert.NotNull(HotCornerGate.BlockReason(false, true, false, 5, false, false, false, false));
        Assert.NotNull(HotCornerGate.BlockReason(true, false, false, 5, false, false, false, false));
        Assert.NotNull(HotCornerGate.BlockReason(true, true, true, 5, false, false, false, false));
        Assert.NotNull(HotCornerGate.BlockReason(true, true, false, 5, false, false, true, false));
        Assert.NotNull(HotCornerGate.BlockReason(true, true, false, 5, false, false, false, true));
    }
    [Fact] public void FullScreenUsesMonitorNotWorkArea()
    {
        Assert.True(HotCornerGeometry.CoversMonitor(new(-8, -8, 1928, 1088), Single));
        Assert.False(HotCornerGeometry.CoversMonitor(new(0, 0, 1920, 1040), Single));
        Assert.False(HotCornerGeometry.CoversMonitor(default, Single));
    }
    [Fact] public void MouseSnapshotRetainsTransitionsAndSessionBoundaryInvalidatesPosition()
    {
        var mouse = new HotCornerMouseState(); mouse.SetHookAvailable(true); Assert.False(mouse.Read().HasPosition);
        mouse.Observe(0, 0, 0x200, 0); long revision = mouse.Read().Revision;
        mouse.Observe(0, 0, 0x200, 0); Assert.Equal(revision, mouse.Read().Revision);
        mouse.Observe(0, 0, 0x201, 0); Assert.Equal(1u, mouse.Read().Buttons);
        mouse.Observe(0, 0, 0x202, 0); Assert.Equal(0u, mouse.Read().Buttons); Assert.Equal(revision + 2, mouse.Read().Revision);
        mouse.Observe(0, 0, 0x20B, 2u << 16); Assert.Equal(16u, mouse.Read().Buttons);
        mouse.SetSessionBlocked(true); Assert.True(mouse.Read().SessionBlocked); Assert.False(mouse.Read().HasPosition);
        Assert.Equal(0u, mouse.Read().Buttons); // a release lost across lock/unlock cannot leave a permanent drag latch
        mouse.SetSessionBlocked(false); Assert.False(mouse.Read().HasPosition);
        mouse.SetHookAvailable(false); Assert.False(mouse.Read().HookAvailable);
        long boundary = mouse.Read().BoundaryRevision;
        mouse.SetHookAvailable(true); mouse.Observe(0, 0, 0x200, 0);
        Assert.True(mouse.Read().BoundaryRevision > boundary); // a stop/start between polls remains observable
        boundary = mouse.Read().BoundaryRevision;
        mouse.Observe(100, 100, 0x200, 0); Assert.Equal(boundary, mouse.Read().BoundaryRevision);
    }
    [Fact] public void QueuedActionsExpireInsteadOfFiringAfterUiDelay()
    {
        Assert.True(HotCornerTrigger.ActionIsFresh(100, 100)); Assert.True(HotCornerTrigger.ActionIsFresh(100, 350));
        Assert.False(HotCornerTrigger.ActionIsFresh(100, 351)); Assert.False(HotCornerTrigger.ActionIsFresh(100, 99));
        Assert.True(HotCornerTrigger.ActionIsFresh(100, 200, 1, 1));
        Assert.False(HotCornerTrigger.ActionIsFresh(100, 200, 1, 2)); // foreground changed away/back while Focus was queued
    }
    [Theory]
    [InlineData(false, false, false, false, false)] [InlineData(false, false, true, true, false)]
    [InlineData(false, true, false, false, true)] [InlineData(false, true, true, true, true)]
    [InlineData(true, false, false, true, true)] [InlineData(true, false, true, true, true)]
    [InlineData(true, true, false, true, true)] [InlineData(true, true, true, true, true)]
    public void ModulesRequestOnlyTheirRequiredHooks(bool shortcuts, bool preview, bool corners, bool mouse, bool keyboard) =>
        Assert.Equal(new InputHookRequirements(mouse, keyboard), InputHookRequirements.For(shortcuts, preview, corners));
    [Theory] [InlineData(HotCornerAction.TaskView, 9)] [InlineData(HotCornerAction.ShowDesktop, 0x44)]
    public void NativeChordsArePairedAndIgnoredByShortcutModifierTracker(HotCornerAction action, int key)
    {
        var keys = HotCornerActionKeys.For(action);
        Assert.Equal(new ushort[] { 0x5B, (ushort)key, (ushort)key, 0x5B }, keys.Select(k => k.VirtualKey));
        Assert.Equal(new[] { false, false, true, true }, keys.Select(k => k.KeyUp));
        Assert.True(keys[0].Extended); Assert.True(keys[3].Extended);
        var state = new ShortcutKeyboardState();
        foreach (var stroke in keys) Assert.True(state.Observe(stroke.VirtualKey, !stroke.KeyUp, stroke.ExtraInfo).OwnInput);
        Assert.Equal(default, state.Modifiers);
        var cleanup = HotCornerActionKeys.ReleaseAfterFailure(action);
        Assert.Equal(new ushort[] { 0xE8, 0xE8, (ushort)key, 0x5B }, cleanup.Select(k => k.VirtualKey));
        Assert.Equal(new[] { false, true, true, true }, cleanup.Select(k => k.KeyUp));
        Assert.All(cleanup, stroke => Assert.Equal((nuint)ShortcutKeyboardState.InjectionTag, stroke.ExtraInfo));
    }
    [Fact] public void NonKeyboardActionsDoNotInventChords()
    {
        Assert.Empty(HotCornerActionKeys.For(HotCornerAction.Lock)); Assert.Empty(HotCornerActionKeys.For(HotCornerAction.StartFocus));
        Assert.Empty(HotCornerActionKeys.For(HotCornerAction.None));
    }
    [Fact] public void SamplingPauseCannotCompleteDwellAfterSleepOrWorkerStall()
    {
        var trigger = Armed(); trigger.Tick(Origin, 1, 100, Single, On, null);
        Assert.Contains("sample gap", trigger.Tick(Origin, 1, 10000, Single, On, null).Diagnostic!);
        Assert.Null(trigger.Tick(Origin, 1, 10250, Single, On, null).Fired);
        trigger.Tick(new(100, 100), 2, 10300, Single, On, null);
        trigger.Tick(Origin, 3, 10400, Single, On, null);
        Assert.NotNull(trigger.Tick(Origin, 3, 10650, Single, On, null).Fired);
    }
}
