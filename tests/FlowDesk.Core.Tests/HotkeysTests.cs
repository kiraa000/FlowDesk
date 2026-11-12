using FlowDesk.Core.Hotkeys;
using FlowDesk.Core.Model;

namespace FlowDesk.Core.Tests;

public sealed class WindowGeometryTests
{
    private static readonly PixelRect WorkArea = new(0, 0, 1920, 1040);

    [Fact]
    public void SnapHalvesUseFloorAndOverlapByEight()
    {
        Assert.Equal(new PixelRect(0, 0, 964, 1040), WindowGeometry.Snap(WorkArea, WindowDirection.Left));
        Assert.Equal(new PixelRect(956, 0, 964, 1040), WindowGeometry.Snap(WorkArea, WindowDirection.Right));
        Assert.Equal(new PixelRect(0, 0, 1920, 524), WindowGeometry.Snap(WorkArea, WindowDirection.Up));
        Assert.Equal(new PixelRect(0, 516, 1920, 524), WindowGeometry.Snap(WorkArea, WindowDirection.Down));
    }

    [Fact]
    public void SnapHandlesOddWidthAndNegativeOrigin()
    {
        var odd = new PixelRect(0, 0, 1366, 1040);
        Assert.Equal(new PixelRect(0, 0, 687, 1040), WindowGeometry.Snap(odd, WindowDirection.Left));
        Assert.Equal(new PixelRect(679, 0, 687, 1040), WindowGeometry.Snap(odd, WindowDirection.Right));

        var left = new PixelRect(-1920, 0, 1920, 1040);
        Assert.Equal(new PixelRect(-1920, 0, 964, 1040), WindowGeometry.Snap(left, WindowDirection.Left));
        Assert.Equal(new PixelRect(-964, 0, 964, 1040), WindowGeometry.Snap(left, WindowDirection.Right));
        Assert.Equal(new PixelRect(-1920, 516, 1920, 524), WindowGeometry.Snap(left, WindowDirection.Down));
    }

    [Fact]
    public void CenterRoundsHalfPixelsAwayFromZeroAndKeepsSize()
    {
        Assert.Equal(new PixelRect(560, 220, 800, 600),
            WindowGeometry.Center(WorkArea, new PixelRect(100, 100, 800, 600)));
        Assert.Equal(new PixelRect(560, 220, 801, 601),
            WindowGeometry.Center(WorkArea, new PixelRect(100, 100, 801, 601)));
    }

    [Fact]
    public void ResizeGrowsAndShrinksAroundTheCentre()
    {
        Assert.Equal(new PixelRect(68, 76, 864, 648),
            WindowGeometry.Resize(WorkArea, new PixelRect(100, 100, 800, 600), grow: true));
        Assert.Equal(new PixelRect(132, 124, 736, 552),
            WindowGeometry.Resize(WorkArea, new PixelRect(100, 100, 800, 600), grow: false));
    }

    [Fact]
    public void ResizeClampsMinimumSize()
    {
        Assert.Equal(new PixelRect(50, 75, 300, 200),
            WindowGeometry.Resize(WorkArea, new PixelRect(100, 100, 200, 150), grow: true));
    }

    [Fact]
    public void ResizeClampsToWorkArea()
    {
        Assert.Equal(new PixelRect(0, 0, 1920, 1040),
            WindowGeometry.Resize(WorkArea, new PixelRect(0, 0, 1900, 1030), grow: true));
        Assert.Equal(new PixelRect(300, 64, 1620, 972),
            WindowGeometry.Resize(WorkArea, new PixelRect(400, 100, 1500, 900), grow: true));
        Assert.Equal(new PixelRect(40, 68, 1620, 972),
            WindowGeometry.Resize(WorkArea, new PixelRect(100, 300, 1500, 900), grow: true));
    }

    [Fact]
    public void ResizeClampsTopAndLeftEdges()
    {
        Assert.Equal(new PixelRect(0, 0, 864, 648),
            WindowGeometry.Resize(WorkArea, new PixelRect(0, 0, 800, 600), grow: true));
    }

    [Fact]
    public void ResizeClampsToATinyWorkArea()
    {
        Assert.Equal(new PixelRect(0, 0, 200, 150),
            WindowGeometry.Resize(new PixelRect(0, 0, 200, 150), new PixelRect(0, 0, 800, 600), grow: true));
    }

    [Fact]
    public void NegativeHalfPixelsRoundAwayFromZero()
    {
        Assert.Equal(new PixelRect(-1001, -1001, 801, 601),
            WindowGeometry.Center(new PixelRect(-1000, -1000, 800, 600), new PixelRect(0, 0, 801, 601)));
        Assert.Equal(new PixelRect(-933, 76, 875, 648),
            WindowGeometry.Resize(new PixelRect(-1920, 0, 1920, 1040), new PixelRect(-900, 100, 810, 600), grow: true));
    }
}

public sealed class DirectionalFocusTests
{
    private static FocusWindow Window(long handle, PixelRect rect, string title = "Window", string cls = "Win",
        bool visible = true, bool cloaked = false, bool minimized = false, bool tool = false, int z = 0) =>
        new(handle, rect, z, visible, cloaked, minimized, title, cls, tool);

    [Fact]
    public void UsableAppliesEveryExclusion()
    {
        var active = Window(1, new(500, 500, 100, 100));
        var windows = new List<FocusWindow>
        {
            active,
            Window(2, new(0, 0, 100, 100), visible: false),
            Window(3, new(0, 0, 100, 100), cloaked: true),
            Window(4, new(0, 0, 100, 100), minimized: true),
            Window(5, new(0, 0, 100, 100), title: ""),
            Window(6, new(0, 0, 100, 100), cls: "Progman"),
            Window(7, new(0, 0, 100, 100), tool: true),
            Window(8, new(0, 0, 100, 100)),
            Window(9, new(0, 0, 100, 100), cls: "WorkerW"),
            Window(10, new(0, 0, 100, 100), cls: "Shell_TrayWnd"),
            Window(11, new(0, 0, 100, 100), cls: "Shell_SecondaryTrayWnd"),
        };

        var usable = DirectionalFocus.Usable(windows, active.Handle);

        Assert.Single(usable);
        Assert.Equal(8, usable[0].Handle);
    }

    [Fact]
    public void RankAppliesEachDirectionFilter()
    {
        var active = new PixelRect(0, 0, 100, 100); // centre (50,50)
        var windows = new List<FocusWindow>
        {
            Window(40, new PixelRect(-150, 0, 100, 100)),  // left
            Window(41, new PixelRect(250, 0, 100, 100)),   // right
            Window(42, new PixelRect(0, -150, 100, 100)),  // up
            Window(43, new PixelRect(0, 250, 100, 100)),   // down
        };

        Assert.Equal(new long[] { 40 }, DirectionalFocus.Rank(active, windows, WindowDirection.Left).Select(w => w.Handle));
        Assert.Equal(new long[] { 41 }, DirectionalFocus.Rank(active, windows, WindowDirection.Right).Select(w => w.Handle));
        Assert.Equal(new long[] { 42 }, DirectionalFocus.Rank(active, windows, WindowDirection.Up).Select(w => w.Handle));
        Assert.Equal(new long[] { 43 }, DirectionalFocus.Rank(active, windows, WindowDirection.Down).Select(w => w.Handle));
    }

    [Fact]
    public void RankFiltersByDirectionAndWeightsSecondaryByOnePointFive()
    {
        var active = new PixelRect(0, 0, 100, 100); // centre (50,50)
        var windows = new List<FocusWindow>
        {
            Window(10, new PixelRect(100, 0, 100, 100)),   // dx 100, dy 0  -> 100
            Window(11, new PixelRect(60, 150, 100, 100)),  // dx 60, dy 150 -> 285
            Window(12, new PixelRect(10, 130, 100, 100)),  // dx 10, dy 130 -> 205
            Window(13, new PixelRect(-150, 0, 100, 100)),  // left: excluded
        };

        var ranked = DirectionalFocus.Rank(active, windows, WindowDirection.Right);

        Assert.Equal(new long[] { 10, 12, 11 }, ranked.Select(window => window.Handle));
        Assert.Empty(DirectionalFocus.Rank(active, windows, WindowDirection.Up));
    }

    [Fact]
    public void RankKeepsZOrderForEqualScores()
    {
        var active = new PixelRect(0, 0, 100, 100);
        var windows = new List<FocusWindow>
        {
            Window(20, new PixelRect(100, 0, 100, 100)),
            Window(21, new PixelRect(100, 0, 100, 100)),
        };

        Assert.Equal(new long[] { 20, 21 }, DirectionalFocus.Rank(active, windows, WindowDirection.Right).Select(w => w.Handle));
    }

    [Fact]
    public void PickUsesStepsAndFallsBackToTheLastCandidate()
    {
        var active = new PixelRect(0, 0, 100, 100);
        var windows = new List<FocusWindow>
        {
            Window(30, new PixelRect(100, 0, 100, 100)),
            Window(31, new PixelRect(300, 0, 100, 100)),
        };

        Assert.Equal(30, DirectionalFocus.Pick(active, windows, WindowDirection.Right, 1));
        Assert.Equal(31, DirectionalFocus.Pick(active, windows, WindowDirection.Right, 2));
        Assert.Equal(31, DirectionalFocus.Pick(active, windows, WindowDirection.Right, 9));
        Assert.Null(DirectionalFocus.Pick(active, windows, WindowDirection.Left, 1));
        Assert.Null(DirectionalFocus.Pick(active, windows, WindowDirection.Right, 0));
    }
}

public sealed class MultiPressQueueTests
{
    [Fact]
    public void SameDirectionCountsPresses()
    {
        var scheduler = new FakeScheduler();
        var queue = new MultiPressQueue(scheduler);
        (WindowDirection Direction, int Steps)? fired = null;
        queue.Fired += (direction, steps) => fired = (direction, steps);

        queue.Press(WindowDirection.Left);
        queue.Press(WindowDirection.Left);
        queue.Press(WindowDirection.Left);
        scheduler.Timers[^1].Fire();

        Assert.Equal(3, scheduler.Timers.Count);
        Assert.True(scheduler.Timers[0].Disposed);
        Assert.Equal((WindowDirection.Left, 3), fired!.Value);
    }

    [Fact]
    public void ChangingDirectionResetsTheCount()
    {
        var scheduler = new FakeScheduler();
        var queue = new MultiPressQueue(scheduler);
        (WindowDirection Direction, int Steps)? fired = null;
        queue.Fired += (direction, steps) => fired = (direction, steps);

        queue.Press(WindowDirection.Left);
        queue.Press(WindowDirection.Left);
        queue.Press(WindowDirection.Right);
        scheduler.Timers[^1].Fire();

        Assert.Equal((WindowDirection.Right, 1), fired!.Value);
    }

    [Fact]
    public void EachPressRestartsTheTimer()
    {
        var scheduler = new FakeScheduler();
        var queue = new MultiPressQueue(scheduler);
        int steps = 0;
        queue.Fired += (_, count) => steps = count;

        queue.Press(WindowDirection.Up);
        var first = scheduler.Timers[0];
        queue.Press(WindowDirection.Up);
        var second = scheduler.Timers[1];

        first.Fire(); // disposed by the restart, must not report
        second.Fire();

        Assert.True(first.Disposed);
        Assert.Equal(2, steps);
        Assert.Equal(MultiPressQueue.Window, second.Delay);
    }

    [Fact]
    public void StaleTimerCallbackIsRejectedByItsToken()
    {
        var scheduler = new FakeScheduler();
        var queue = new MultiPressQueue(scheduler);
        int steps = 0;
        queue.Fired += (_, count) => steps = count;

        queue.Press(WindowDirection.Up);
        var first = scheduler.Timers[0];
        queue.Press(WindowDirection.Up);
        var second = scheduler.Timers[1];

        first.FireIgnoringDisposal(); // the timer callback still runs after disposal
        Assert.Equal(0, steps);

        second.Fire();
        Assert.Equal(2, steps);
    }

    [Fact]
    public void ResetDropsAPendingSequence()
    {
        var scheduler = new FakeScheduler();
        var queue = new MultiPressQueue(scheduler);
        bool fired = false;
        queue.Fired += (_, _) => fired = true;

        queue.Press(WindowDirection.Down);
        queue.Reset();
        scheduler.Timers[^1].Fire();

        Assert.True(scheduler.Timers[^1].Disposed);
        Assert.False(fired);
    }
}

public sealed class HotkeyCatalogTests
{
    [Fact]
    public void CatalogHasTheExactBindingSet()
    {
        var all = HotkeyCatalog.All;

        Assert.Equal(32, all.Count);
        Assert.Equal(10, all.Count(binding => binding.IsDesktop));
        Assert.Equal(8, all.Count(binding => binding.Id.StartsWith("snap-", StringComparison.Ordinal)));
        Assert.Equal(8, all.Count(binding => binding.Id.StartsWith("focus-", StringComparison.Ordinal)));
        Assert.Single(all, binding => binding.IsMouseWheel);
        Assert.Equal(31, HotkeyCatalog.Registerable.Count());
        Assert.Equal(all.Count, all.Select(binding => binding.Id).Distinct().Count());
    }

    [Fact]
    public void CatalogMatchesEveryModifierAndVirtualKey()
    {
        var alt = HotkeyCatalog.ModAlt;
        var both = alt | HotkeyCatalog.ModShift;
        var expected = new (string Id, uint Modifiers, uint VirtualKey, bool Repeat, bool Pausable)[]
        {
            ("desktop-switch-1", alt, 0x31, false, false),
            ("desktop-switch-2", alt, 0x32, false, false),
            ("desktop-switch-3", alt, 0x33, false, false),
            ("desktop-switch-4", alt, 0x34, false, false),
            ("desktop-switch-5", alt, 0x35, false, false),
            ("desktop-move-1", both, 0x31, false, false),
            ("desktop-move-2", both, 0x32, false, false),
            ("desktop-move-3", both, 0x33, false, false),
            ("desktop-move-4", both, 0x34, false, false),
            ("desktop-move-5", both, 0x35, false, false),
            ("snap-up", alt, 0x57, true, true),
            ("snap-left", alt, 0x41, true, true),
            ("snap-down", alt, 0x53, true, true),
            ("snap-right", alt, 0x44, true, true),
            ("snap-up-arrow", alt, 0x26, true, true),
            ("snap-left-arrow", alt, 0x25, true, true),
            ("snap-down-arrow", alt, 0x28, true, true),
            ("snap-right-arrow", alt, 0x27, true, true),
            ("focus-up", both, 0x57, true, true),
            ("focus-left", both, 0x41, true, true),
            ("focus-down", both, 0x53, true, true),
            ("focus-right", both, 0x44, true, true),
            ("focus-up-arrow", both, 0x26, true, true),
            ("focus-left-arrow", both, 0x25, true, true),
            ("focus-down-arrow", both, 0x28, true, true),
            ("focus-right-arrow", both, 0x27, true, true),
            ("maximize-toggle", alt, 0x46, false, true),
            ("minimize", alt, 0x4D, false, true),
            ("next-window", alt, 0x51, false, true),
            ("center", both, 0x20, false, true),
            ("pause-toggle", both, 0x30, false, false),
            ("resize-wheel", both, 0x00, true, false),
        };

        var actual = HotkeyCatalog.All
            .Select(binding => (binding.Id, binding.Modifiers, binding.VirtualKey, binding.Repeat, binding.Pausable))
            .ToArray();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void RepeatAndPausableFlagsMatchTheBrief()
    {
        foreach (var binding in HotkeyCatalog.All)
        {
            bool directional = binding.Id.StartsWith("snap-", StringComparison.Ordinal)
                || binding.Id.StartsWith("focus-", StringComparison.Ordinal);
            Assert.Equal(directional || binding.IsMouseWheel, binding.Repeat);
            Assert.Equal(directional
                || binding.Id is "maximize-toggle" or "minimize" or "next-window" or "center", binding.Pausable);
            Assert.Equal(0u, binding.Modifiers & 0x4000u);
        }
    }

    [Fact]
    public void DesktopBindingsCarryTheirNumberAndModifiers()
    {
        var switchThree = HotkeyCatalog.Find("desktop-switch-3")!;
        Assert.Equal(3, switchThree.DesktopNumber);
        Assert.Equal(HotkeyCatalog.ModAlt, switchThree.Modifiers);
        Assert.Equal((uint)'3', switchThree.VirtualKey);
        Assert.False(switchThree.Repeat);

        var moveFive = HotkeyCatalog.Find("desktop-move-5")!;
        Assert.Equal(HotkeyCatalog.ModAlt | HotkeyCatalog.ModShift, moveFive.Modifiers);
        Assert.True(moveFive.IsDesktop);
    }

    [Fact]
    public void DirectionHelpersSeparateSnapFromFocus()
    {
        Assert.True(HotkeyCatalog.TryGetSnapDirection(HotkeyCatalog.Find("snap-right-arrow")!, out var snap));
        Assert.Equal(WindowDirection.Right, snap);
        Assert.False(HotkeyCatalog.TryGetSnapDirection(HotkeyCatalog.Find("focus-right")!, out _));

        Assert.True(HotkeyCatalog.TryGetFocusDirection(HotkeyCatalog.Find("focus-down")!, out var focus));
        Assert.Equal(WindowDirection.Down, focus);
        Assert.False(HotkeyCatalog.TryGetFocusDirection(HotkeyCatalog.Find("center")!, out _));
    }
}

public sealed class WindowShortcutActionsTests
{
    private static (WindowShortcutActions Actions, FakeWindowOps Ops, FakeVirtualDesktops Desktops, RecordingDelay Delay) Build()
    {
        var ops = new FakeWindowOps();
        var desktops = new FakeVirtualDesktops();
        var delay = new RecordingDelay();
        return (new WindowShortcutActions(ops, desktops, delay), ops, desktops, delay);
    }

    [Fact]
    public void SwitchingToTheCurrentDesktopDoesNothing()
    {
        var (actions, ops, desktops, _) = Build();
        desktops.Count = 3;
        desktops.Current = 1;

        actions.Execute(HotkeyCatalog.Find("desktop-switch-2")!, CancellationToken.None);

        Assert.Empty(desktops.Log);
        Assert.Empty(ops.Log);
    }

    [Fact]
    public void SwitchingCreatesMissingDesktopsThenGoesWithoutFocusCorrection()
    {
        var (actions, ops, desktops, _) = Build();
        desktops.Count = 1;
        desktops.Current = 0;

        actions.Execute(HotkeyCatalog.Find("desktop-switch-3")!, CancellationToken.None);

        Assert.Equal(new[] { "create", "create", "goto:2" }, desktops.Log);
        Assert.Empty(ops.Log);
    }

    [Fact]
    public void SwitchingStopsWhenDesktopCreationFails()
    {
        var (actions, _, desktops, _) = Build();
        desktops.Count = 1;
        desktops.CreateFails = true;

        actions.Execute(HotkeyCatalog.Find("desktop-switch-3")!, CancellationToken.None);

        Assert.Equal(new[] { "create" }, desktops.Log);
    }

    [Fact]
    public void MovingToDesktopRunsTheFlowInOrder()
    {
        var (actions, ops, desktops, delay) = Build();
        long active = ops.AddWindow(new PixelRect(100, 100, 800, 600));
        ops.Foreground = active;
        desktops.Count = 2;

        actions.Execute(HotkeyCatalog.Find("desktop-move-2")!, CancellationToken.None);

        Assert.Equal(new[] { "movewindow:100:1", "goto:1" }, desktops.Log);
        Assert.Equal(active, ops.Activated);
        Assert.Equal(TimeSpan.FromMilliseconds(100), Assert.Single(delay.Waits));
        Assert.Contains("activate:100", ops.Log);
    }

    [Fact]
    public void MovingToDesktopStopsWhenMoveWindowFails()
    {
        var (actions, ops, desktops, delay) = Build();
        long active = ops.AddWindow(new PixelRect(100, 100, 800, 600));
        ops.Foreground = active;
        desktops.Count = 2;
        desktops.MoveFails = true;

        actions.Execute(HotkeyCatalog.Find("desktop-move-2")!, CancellationToken.None);

        Assert.Equal(new[] { "movewindow:100:1" }, desktops.Log);
        Assert.Null(ops.Activated);
        Assert.Empty(delay.Waits);
    }

    [Fact]
    public void PauseGatesOnlyPausableBindings()
    {
        var (actions, ops, desktops, _) = Build();
        long active = ops.AddWindow(new PixelRect(100, 100, 800, 600));
        ops.Foreground = active;
        desktops.Count = 2;
        actions.TogglePause();

        actions.Execute(HotkeyCatalog.Find("snap-left")!, CancellationToken.None);
        actions.Execute(HotkeyCatalog.Find("maximize-toggle")!, CancellationToken.None);
        Assert.Empty(ops.Log);

        actions.Execute(HotkeyCatalog.Find("desktop-move-2")!, CancellationToken.None);
        actions.ResizeWindow(grow: true, CancellationToken.None);

        Assert.Contains("movewindow:100:1", desktops.Log);
        Assert.Contains(ops.Moves, rect => rect.Width > 800);
    }

    [Fact]
    public void NextWindowPicksTheFirstUsableWindowInZOrder()
    {
        var (actions, ops, _, delay) = Build();
        long active = ops.AddWindow(new PixelRect(0, 0, 200, 200));
        ops.Foreground = active;
        ops.AddWindow(new PixelRect(0, 0, 200, 200), tool: true);
        ops.AddWindow(new PixelRect(0, 0, 200, 200), visible: false);
        long next = ops.AddWindow(new PixelRect(400, 0, 200, 200));
        ops.AddWindow(new PixelRect(800, 0, 200, 200));

        actions.Execute(HotkeyCatalog.Find("next-window")!, CancellationToken.None);

        Assert.Contains($"minimize:{active}", ops.Log);
        Assert.Equal(next, ops.Activated);
        Assert.Equal(TimeSpan.FromMilliseconds(40), Assert.Single(delay.Waits));
    }

    [Fact]
    public void ShellWindowsAreSkipped()
    {
        var (actions, ops, _, _) = Build();
        long active = ops.AddWindow(new PixelRect(0, 0, 800, 600), cls: "Progman");
        ops.Foreground = active;
        ops.ShellHandles.Add(active);

        actions.Execute(HotkeyCatalog.Find("snap-left")!, CancellationToken.None);
        actions.Execute(HotkeyCatalog.Find("minimize")!, CancellationToken.None);

        Assert.Empty(ops.Log);
    }

    [Fact]
    public void SnapRestoresAMaximizedWindowThenMovesToTheHalf()
    {
        var (actions, ops, _, delay) = Build();
        long active = ops.AddWindow(new PixelRect(0, 0, 1920, 1040), maximized: true);
        ops.Foreground = active;
        ops.RestoredRects[active] = new PixelRect(100, 100, 800, 600);

        actions.Execute(HotkeyCatalog.Find("snap-left")!, CancellationToken.None);

        Assert.Equal(TimeSpan.FromMilliseconds(25), Assert.Single(delay.Waits));
        Assert.Equal(WindowGeometry.Snap(new PixelRect(0, 0, 1920, 1040), WindowDirection.Left), ops.Moves[^1]);
    }

    [Fact]
    public void CenterWaitsAfterRestoringAndKeepsTheSize()
    {
        var (actions, ops, _, delay) = Build();
        long active = ops.AddWindow(new PixelRect(0, 0, 1920, 1040), maximized: true);
        ops.Foreground = active;
        ops.RestoredRects[active] = new PixelRect(100, 100, 800, 600);

        actions.Execute(HotkeyCatalog.Find("center")!, CancellationToken.None);

        Assert.Equal(TimeSpan.FromMilliseconds(30), Assert.Single(delay.Waits));
        Assert.Equal(WindowGeometry.Center(new PixelRect(0, 0, 1920, 1040), new PixelRect(100, 100, 800, 600)), ops.Moves[^1]);
    }

    [Fact]
    public void ResizeRestoresMaximizedButSkipsMinimizedWindows()
    {
        var (actions, ops, _, delay) = Build();
        long maximized = ops.AddWindow(new PixelRect(0, 0, 1920, 1040), maximized: true);
        ops.Foreground = maximized;
        ops.RestoredRects[maximized] = new PixelRect(100, 100, 800, 600);

        actions.ResizeWindow(grow: true, CancellationToken.None);
        Assert.Equal(TimeSpan.FromMilliseconds(25), Assert.Single(delay.Waits));
        Assert.Single(ops.Moves);

        ops.Moves.Clear();
        long minimized = ops.AddWindow(new PixelRect(0, 0, 800, 600), minimized: true);
        ops.Foreground = minimized;
        delay.Waits.Clear();

        actions.ResizeWindow(grow: true, CancellationToken.None);
        Assert.Empty(delay.Waits);
        Assert.Empty(ops.Moves);
    }

    [Fact]
    public void ToggleMaximizeRestoresOrMaximizes()
    {
        var (actions, ops, _, _) = Build();
        long active = ops.AddWindow(new PixelRect(100, 100, 800, 600));
        ops.Foreground = active;

        actions.Execute(HotkeyCatalog.Find("maximize-toggle")!, CancellationToken.None);
        Assert.Contains($"maximize:{active}", ops.Log);

        ops.Metrics[active] = ops.Metrics[active] with { IsMaximized = true };
        actions.Execute(HotkeyCatalog.Find("maximize-toggle")!, CancellationToken.None);
        Assert.Contains($"restore:{active}", ops.Log);
    }

    [Fact]
    public void FocusPicksTheStepsCandidateAndActivatesIt()
    {
        var (actions, ops, _, _) = Build();
        long active = ops.AddWindow(new PixelRect(0, 0, 100, 100));
        ops.Foreground = active;
        long first = ops.AddWindow(new PixelRect(200, 0, 100, 100));
        long second = ops.AddWindow(new PixelRect(500, 0, 100, 100));

        actions.Focus(WindowDirection.Right, 2, CancellationToken.None);

        Assert.Equal(second, ops.Activated);
        Assert.NotEqual(first, ops.Activated);
    }

    [Fact]
    public void CancelledWorkStopsBeforeTouchingWindows()
    {
        var (actions, ops, desktops, _) = Build();
        long active = ops.AddWindow(new PixelRect(100, 100, 800, 600));
        ops.Foreground = active;
        using var source = new CancellationTokenSource();
        source.Cancel();

        actions.Execute(HotkeyCatalog.Find("snap-left")!, source.Token);
        actions.Execute(HotkeyCatalog.Find("desktop-switch-2")!, source.Token);
        actions.Focus(WindowDirection.Right, 1, source.Token);

        Assert.Empty(ops.Log);
        Assert.Empty(desktops.Log);
        Assert.Null(ops.Activated);
    }

    [Fact]
    public void CancellingDuringDesktopCountStopsBeforeCreate()
    {
        var (actions, _, desktops, _) = Build();
        desktops.Count = 1;
        using var source = new CancellationTokenSource();
        desktops.OnGetCount = source.Cancel;

        actions.Execute(HotkeyCatalog.Find("desktop-switch-3")!, source.Token);

        Assert.Empty(desktops.Log);
    }

    [Fact]
    public void CancellingDuringCreateStopsBeforeGoTo()
    {
        var (actions, _, desktops, _) = Build();
        desktops.Count = 1;
        using var source = new CancellationTokenSource();
        desktops.OnCreate = source.Cancel;

        actions.Execute(HotkeyCatalog.Find("desktop-switch-3")!, source.Token);

        Assert.Equal(new[] { "create" }, desktops.Log);
    }

    [Fact]
    public void CancellingDuringMoveWindowStopsBeforeGoTo()
    {
        var (actions, ops, desktops, delay) = Build();
        long active = ops.AddWindow(new PixelRect(100, 100, 800, 600));
        ops.Foreground = active;
        desktops.Count = 2;
        using var source = new CancellationTokenSource();
        desktops.OnMoveWindow = source.Cancel;

        actions.Execute(HotkeyCatalog.Find("desktop-move-2")!, source.Token);

        Assert.Equal(new[] { "movewindow:100:1" }, desktops.Log);
        Assert.Empty(delay.Waits);
        Assert.Null(ops.Activated);
    }

    [Fact]
    public void CancelledRestoreDelayStopsMoves()
    {
        var (actions, ops, _, delay) = Build();
        long active = ops.AddWindow(new PixelRect(0, 0, 1920, 1040), maximized: true);
        ops.Foreground = active;
        ops.RestoredRects[active] = new PixelRect(100, 100, 800, 600);
        delay.Cancelled = true;

        actions.Execute(HotkeyCatalog.Find("snap-left")!, CancellationToken.None);
        ops.Metrics[active] = ops.Metrics[active] with { IsMaximized = true };
        actions.ResizeWindow(grow: true, CancellationToken.None);

        Assert.Contains($"restore:{active}", ops.Log);
        Assert.Empty(ops.Moves);
    }
}
