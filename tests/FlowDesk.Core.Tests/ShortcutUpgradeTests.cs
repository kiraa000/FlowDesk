using FlowDesk.Core.Hotkeys;
using FlowDesk.Core.Model;

namespace FlowDesk.Core.Tests;

public sealed class WheelGesturesTests
{
    [Theory]
    [InlineData(true, false, false, false, WheelGesture.Desktop)]
    [InlineData(true, false, true, false, WheelGesture.MoveDesktop)]
    [InlineData(true, true, false, false, WheelGesture.Search)]
    [InlineData(false, true, true, false, WheelGesture.Resize)]
    [InlineData(false, false, false, false, WheelGesture.None)]
    [InlineData(true, true, true, false, WheelGesture.None)]
    [InlineData(true, false, false, true, WheelGesture.None)]
    [InlineData(false, true, false, false, WheelGesture.None)]
    public void RoutesOnlyExactModifiers(bool win, bool alt, bool shift, bool ctrl, WheelGesture expected) =>
        Assert.Equal(expected, WheelGestures.Route(win, alt, shift, ctrl));

    [Theory]
    [InlineData(false, false, 123)]
    [InlineData(false, true, 250)]
    [InlineData(true, false, 40)]
    public void ThrottleSwallowsButDoesNotAcceptEarlyEvents(bool alt, bool shift, int milliseconds)
    {
        var state = new WheelGestures();
        Assert.True(state.Accept(true, alt, shift, false, 0).Accepted);
        var early = state.Accept(true, alt, shift, false, milliseconds - 1);
        Assert.NotEqual(WheelGesture.None, early.Gesture);
        Assert.False(early.Accepted);
        Assert.True(state.Accept(true, alt, shift, false, milliseconds).Accepted);
    }

    [Fact]
    public void MenuMaskOncePerWinHoldAcrossGesturesAndAgainAfterRelease()
    {
        var state = new WheelGestures();
        Assert.True(state.Accept(true, false, false, false, 0).MaskMenu);
        Assert.False(state.Accept(true, false, false, false, 500).MaskMenu);
        Assert.False(state.Accept(true, false, true, false, 1000).MaskMenu);
        state.ObserveWin(false);
        Assert.True(state.Accept(true, false, true, false, 1500).MaskMenu);
    }

    [Fact]
    public void NewAltHoldIsMaskedEvenWhenWinWasAlreadyMasked()
    {
        var state = new WheelGestures();
        state.Accept(true, false, false, false, 0);
        Assert.True(state.Accept(true, true, false, false, 100).MaskMenu);
        Assert.False(state.Accept(true, true, false, false, 150).MaskMenu);
        state.ObserveAlt(false);
        Assert.True(state.Accept(true, true, false, false, 200).MaskMenu);
    }

    [Fact]
    public void FreshWinHoldAcceptsAndMasksEvenInsidePreviousHoldsCooldown()
    {
        var state = new WheelGestures();
        state.Accept(true, false, false, false, 0);
        state.ObserveWin(false);
        var first = state.Accept(true, false, false, false, 50);
        Assert.True(first.Accepted);
        Assert.True(first.MaskMenu);
    }
}

public sealed class WindowSearchSessionTests
{
    private static readonly PixelRect Rect = new(100, 100, 800, 600);
    private static (WindowSearchSession Session, FakeWindowOps Ops, FakeVirtualDesktops Desktops, long A, long B, long C) Build()
    {
        var ops = new FakeWindowOps();
        long a = ops.AddWindow(Rect), b = ops.AddWindow(Rect), c = ops.AddWindow(Rect, minimized: true);
        ops.Foreground = a;
        var desktops = new FakeVirtualDesktops();
        return (new(ops, desktops), ops, desktops, a, b, c);
    }

    [Fact]
    public void UpChainRestoresAndDownUndoesWithoutRebuildingSnapshot()
    {
        var (session, ops, _, a, b, c) = Build();
        session.Wheel(true);
        Assert.Equal(b, ops.Foreground);
        session.Wheel(true);
        Assert.Equal(c, ops.Foreground);
        Assert.Contains($"restore:{c}", ops.Log);
        session.Wheel(false);
        Assert.True(ops.Metrics[c].IsMinimized);
        Assert.Equal(b, ops.Foreground);
        session.Wheel(false);
        Assert.Equal(a, ops.Foreground);
        session.Wheel(false);
        Assert.False(ops.Metrics[a].IsMinimized);
        Assert.Equal(1, ops.EnumerationCount);
    }

    [Fact]
    public void SpontaneousDownHistorySurvivesReleaseAndUpRestoresMostRecent()
    {
        var (session, ops, _, a, b, _) = Build();
        session.Wheel(false);
        Assert.True(ops.Metrics[a].IsMinimized);
        Assert.Equal(b, ops.Foreground);
        session.Reset(); // either key-up; no OS hook required by this headless test
        session.Wheel(true);
        Assert.Equal(a, ops.Foreground);
        Assert.False(ops.Metrics[a].IsMinimized);
        Assert.Equal(2, ops.EnumerationCount);
    }

    [Fact]
    public void StaleHistoryAndDestroyedCandidatesAreSkipped()
    {
        var (session, ops, _, a, b, c) = Build();
        session.Wheel(false); // minimize A
        session.Wheel(false); // minimize B; C is minimized, so no replacement
        ops.Metrics.Remove(b);
        session.Reset();
        ops.Foreground = c;
        session.Wheel(true);
        Assert.Equal(a, ops.Foreground); // skips dead B, restores older A
        ops.Metrics.Remove(c);
        session.Wheel(true);
        Assert.Equal(a, ops.Foreground);
    }

    [Fact]
    public void RestoredHistoryEntriesAreSkipped()
    {
        var (session, ops, _, a, _, c) = Build();
        session.Wheel(false);
        ops.Restore(a); // user restored it outside the search
        session.Reset();
        session.Wheel(true);
        Assert.Equal(a, ops.Foreground); // normal snapshot next candidate, not history restore
        Assert.Equal(1, ops.Log.Count(s => s == $"restore:{a}"));
        session.Wheel(true);
        Assert.Equal(c, ops.Foreground);
    }

    [Fact]
    public void CandidatesFilterDesktopStyleTitleToolAndShellButAllowMinimized()
    {
        var ops = new FakeWindowOps();
        long active = ops.AddWindow(Rect);
        ops.Foreground = active;
        ops.AddWindow(Rect, visible: false);
        ops.AddWindow(Rect, title: "");
        ops.AddWindow(Rect, tool: true);
        ops.AddWindow(Rect, cls: "Progman");
        ops.AddWindow(Rect, cls: "WorkerW");
        long elsewhere = ops.AddWindow(Rect);
        long minimized = ops.AddWindow(Rect, minimized: true);
        var desktops = new FakeVirtualDesktops();
        desktops.OnCurrent[elsewhere] = false;
        var session = new WindowSearchSession(ops, desktops);
        session.Wheel(true);
        Assert.Equal(minimized, ops.Foreground);
        long late = ops.AddWindow(Rect);
        session.Wheel(true);
        Assert.NotEqual(late, ops.Foreground);
        Assert.Equal(1, ops.EnumerationCount);
        session.Reset();
        session.Wheel(true);
        Assert.Equal(active, ops.Foreground);
        Assert.Equal(2, ops.EnumerationCount);
    }

    [Fact]
    public void ExternalDesktopChangeStartsANewSnapshot()
    {
        var (session, ops, desktops, _, _, _) = Build();
        session.Wheel(true);
        desktops.Current = 1;
        session.Wheel(true);
        Assert.Equal(2, ops.EnumerationCount);
    }

    [Theory]
    [InlineData("maximize-toggle")]
    [InlineData("desktop-switch-2")]
    [InlineData("desktop-move-2")]
    public void MaximizeAndDesktopActionsResetSearch(string id)
    {
        var (_, ops, desktops, _, _, _) = Build();
        desktops.Count = 2;
        var actions = new WindowShortcutActions(ops, desktops, new RecordingDelay());
        actions.Search.Wheel(true);
        actions.Execute(HotkeyCatalog.Find(id)!, CancellationToken.None);
        int before = ops.EnumerationCount;
        actions.Search.Wheel(true);
        Assert.Equal(before + 1, ops.EnumerationCount);
    }
}

public sealed class SnapCycleTests
{
    private static readonly PixelRect Area = new(-1920, 0, 1920, 1040);

    [Theory]
    [InlineData(WindowDirection.Left)]
    [InlineData(WindowDirection.Right)]
    [InlineData(WindowDirection.Up)]
    [InlineData(WindowDirection.Down)]
    public void CycleUsesHalfThirdTwoThirdsHalf(WindowDirection direction)
    {
        var ops = new FakeWindowOps();
        long handle = ops.AddWindow(new(-1500, 100, 800, 600), Area);
        ops.Foreground = handle;
        var actions = new WindowShortcutActions(ops, null, new RecordingDelay());
        var binding = new HotkeyBinding("snap-" + direction.ToString().ToLowerInvariant(), 1, 0, true, true, "", "");
        foreach (int step in new[] { 0, 1, 2, 0 })
        {
            actions.Execute(binding, CancellationToken.None);
            Assert.Equal(WindowGeometry.Snap(Area, direction, step), ops.Moves[^1]);
        }
        int extent = direction is WindowDirection.Left or WindowDirection.Right ? ops.Moves[1].Width : ops.Moves[1].Height;
        Assert.InRange(extent, (direction is WindowDirection.Left or WindowDirection.Right ? Area.Width : Area.Height) / 3, 
            (direction is WindowDirection.Left or WindowDirection.Right ? Area.Width : Area.Height) / 3 + 8);
    }

    [Fact]
    public void ManualMoveDirectionChangeAndAreaChangeRestartWhileMinorRoundingDoesNot()
    {
        var state = new SnapCycle();
        var rect = WindowGeometry.Snap(Area, WindowDirection.Left);
        state.Remember(1, WindowDirection.Left, 0, rect, Area);
        Assert.Equal(1, state.Next(1, WindowDirection.Left, rect with { X = rect.X + 3 }, Area));
        Assert.Equal(0, state.Next(1, WindowDirection.Left, rect with { X = rect.X + 20 }, Area));
        Assert.Equal(0, state.Next(1, WindowDirection.Right, rect, Area));
        Assert.Equal(0, state.Next(1, WindowDirection.Left, rect, Area with { Width = 1800 }));
        Assert.Equal(0, state.Next(2, WindowDirection.Left, rect, Area));
    }

    [Fact]
    public void RefusedSizeStillAdvancesUsingObservedRect()
    {
        var ops = new FakeWindowOps();
        long handle = ops.AddWindow(new(-1500, 100, 1000, 600), Area);
        ops.Foreground = handle;
        ops.MoveFailures.Add(handle);
        var actions = new WindowShortcutActions(ops, null, new RecordingDelay());
        foreach (int step in new[] { 0, 1, 2, 0 })
        {
            actions.Execute(HotkeyCatalog.Find("snap-left")!, CancellationToken.None);
            Assert.Equal(WindowGeometry.Snap(Area, WindowDirection.Left, step), ops.Moves[^1]);
        }
    }

    [Theory]
    [InlineData("resize-wheel")]
    [InlineData("center")]
    [InlineData("desktop-move-1")]
    [InlineData("maximize-toggle")]
    public void OtherSizingAndDesktopMoveClearTheCycle(string id)
    {
        var ops = new FakeWindowOps();
        long handle = ops.AddWindow(new(-1500, 100, 800, 600), Area);
        ops.Foreground = handle;
        var actions = new WindowShortcutActions(ops, new FakeVirtualDesktops(), new RecordingDelay());
        actions.Execute(HotkeyCatalog.Find("snap-left")!, CancellationToken.None);
        actions.Execute(HotkeyCatalog.Find(id)!, CancellationToken.None);
        actions.Execute(HotkeyCatalog.Find("snap-left")!, CancellationToken.None);
        Assert.Equal(WindowGeometry.Snap(Area, WindowDirection.Left, 0), ops.Moves[^1]);
    }
}

public sealed class DesktopWheelTests
{
    [Theory]
    [InlineData(3, 0, true, 2)]
    [InlineData(3, 2, false, 0)]
    [InlineData(1, 0, true, 0)]
    [InlineData(0, 0, false, 0)]
    public void SwitchWrapsActualCountAndFocusesANewDesktopWindow(int count, int current, bool previous, int expected)
    {
        var desktops = new FakeVirtualDesktops { Count = count, Current = current };
        var ops = new FakeWindowOps();
        long next = ops.AddWindow(new(0, 0, 800, 600));
        var actions = new WindowShortcutActions(ops, desktops, new RecordingDelay());
        actions.DesktopWheel(previous, false, CancellationToken.None);
        Assert.Equal(expected, desktops.Current);
        Assert.Equal(0, desktops.CreateCalls);
        if (current != expected) Assert.Equal(next, ops.Activated);
    }

    [Fact]
    public void MoveUsesExistingMoveAndFollowEvenWhilePaused()
    {
        var desktops = new FakeVirtualDesktops { Count = 4, Current = 0 };
        var ops = new FakeWindowOps();
        long active = ops.AddWindow(new(0, 0, 800, 600));
        ops.Foreground = active;
        var actions = new WindowShortcutActions(ops, desktops, new RecordingDelay());
        actions.SetPaused(true);
        actions.DesktopWheel(true, true, CancellationToken.None);
        Assert.Equal(new[] { 3 }, desktops.Moves);
        Assert.Equal(3, desktops.Current);
        Assert.Equal(active, ops.Activated);
    }

    [Fact]
    public void SwitchSkipsOtherDesktopAndRestoresMinimizedFallback()
    {
        var desktops = new FakeVirtualDesktops { Count = 2 };
        var ops = new FakeWindowOps();
        long elsewhere = ops.AddWindow(new(0, 0, 800, 600));
        desktops.OnCurrent[elsewhere] = false;
        long target = ops.AddWindow(new(0, 0, 800, 600), minimized: true);
        var actions = new WindowShortcutActions(ops, desktops, new RecordingDelay());
        actions.DesktopWheel(false, false, CancellationToken.None);
        Assert.Equal(target, ops.Activated);
        Assert.Contains($"restore:{target}", ops.Log);
    }
}
