using FlowDesk.Core.Hotkeys;
using FlowDesk.Core.Model;

namespace FlowDesk.Core.Tests;

/// <summary>Pure planner, apply/verify transaction, drag follow and action wiring tests for TASK-033.</summary>
public sealed class EdgeResizeTests
{
    private static PixelRect R(int x, int y, int w, int h) => new(x, y, w, h);
    private static PixelRect Area() => R(0, 0, 1920, 1040);

    private static EdgeWindow W(long handle, PixelRect frame, double scale = 1, int minWidth = 0,
        int minHeight = 0, bool fixedWindow = false, FrameInsets insets = default) =>
        new(handle, frame, insets, scale, minWidth, minHeight, fixedWindow);

    [Fact]
    public void RightArrowGrowsActiveAndNeighbourTogether()
    {
        var area = Area();
        var active = W(1, R(0, 0, 960, 1040));
        var neighbour = W(2, R(960, 0, 960, 1040));

        var plan = EdgeResize.Keyboard(active, area, [neighbour], WindowDirection.Right);

        var line = Assert.Single(plan.Lines);
        Assert.Equal(WindowDirection.Right, line.Edge);
        Assert.Equal(96, line.Delta);
        Assert.Equal(R(0, 0, 1056, 1040), plan.Frames[active.Handle]);
        Assert.Equal(R(1056, 0, 864, 1040), plan.Frames[neighbour.Handle]);
    }

    [Fact]
    public void ArrowAtOuterBorderShrinksFromTheOppositeEdge()
    {
        var area = Area();

        // Active right half touches the work-area right border: Right moves its LEFT edge right.
        var rightHalf = W(1, R(960, 0, 960, 1040));
        var leftHalf = W(2, R(0, 0, 960, 1040));
        var rightBorder = EdgeResize.Keyboard(rightHalf, area, [leftHalf], WindowDirection.Right);
        var rightLine = Assert.Single(rightBorder.Lines);
        Assert.Equal(WindowDirection.Left, rightLine.Edge);
        Assert.Equal(96, rightLine.Delta);
        Assert.Equal(R(1056, 0, 864, 1040), rightBorder.Frames[rightHalf.Handle]);
        Assert.Equal(R(0, 0, 1056, 1040), rightBorder.Frames[leftHalf.Handle]);

        // Active left half touches the work-area left border: Left moves its RIGHT edge left.
        var leftBorder = EdgeResize.Keyboard(leftHalf, area, [rightHalf], WindowDirection.Left);
        var leftLine = Assert.Single(leftBorder.Lines);
        Assert.Equal(WindowDirection.Right, leftLine.Edge);
        Assert.Equal(-96, leftLine.Delta);
        Assert.Equal(R(0, 0, 864, 1040), leftBorder.Frames[leftHalf.Handle]);
        Assert.Equal(R(864, 0, 1056, 1040), leftBorder.Frames[rightHalf.Handle]);
    }

    [Fact]
    public void UpAndDownArrowsMirrorTheBorderFallback()
    {
        var area = Area();

        // Active top half touches the top border: Up moves its BOTTOM edge up.
        var topHalf = W(1, R(0, 0, 1920, 520));
        var bottomHalf = W(2, R(0, 520, 1920, 520));
        var topBorder = EdgeResize.Keyboard(topHalf, area, [bottomHalf], WindowDirection.Up);
        var upLine = Assert.Single(topBorder.Lines);
        Assert.Equal(WindowDirection.Down, upLine.Edge);
        Assert.Equal(-52, upLine.Delta);
        Assert.Equal(R(0, 0, 1920, 468), topBorder.Frames[topHalf.Handle]);
        Assert.Equal(R(0, 468, 1920, 572), topBorder.Frames[bottomHalf.Handle]);

        // Active bottom half touches the bottom border: Down moves its TOP edge down.
        var bottomBorder = EdgeResize.Keyboard(bottomHalf, area, [topHalf], WindowDirection.Down);
        var downLine = Assert.Single(bottomBorder.Lines);
        Assert.Equal(WindowDirection.Up, downLine.Edge);
        Assert.Equal(52, downLine.Delta);
        Assert.Equal(R(0, 572, 1920, 468), bottomBorder.Frames[bottomHalf.Handle]);
        Assert.Equal(R(0, 0, 1920, 572), bottomBorder.Frames[topHalf.Handle]);
    }

    [Fact]
    public void ThirdsMoveTheSharedDividerByFivePercent()
    {
        var area = Area();
        var active = W(1, R(0, 0, 640, 1040));
        var neighbour = W(2, R(640, 0, 1280, 1040));

        var plan = EdgeResize.Keyboard(active, area, [neighbour], WindowDirection.Right);

        Assert.Equal(R(0, 0, 736, 1040), plan.Frames[active.Handle]);
        Assert.Equal(R(736, 0, 1184, 1040), plan.Frames[neighbour.Handle]);
    }

    [Fact]
    public void ThreeColumnsOnlyMoveTheNearestNeighbour()
    {
        var area = Area();
        var active = W(1, R(0, 0, 640, 1040));
        var middle = W(2, R(640, 0, 640, 1040));
        var far = W(3, R(1280, 0, 640, 1040));

        var plan = EdgeResize.Keyboard(active, area, [middle, far], WindowDirection.Right);

        Assert.Equal(R(0, 0, 736, 1040), plan.Frames[active.Handle]);
        Assert.Equal(R(736, 0, 544, 1040), plan.Frames[middle.Handle]);
        Assert.False(plan.Frames.ContainsKey(far.Handle));
        Assert.Equal(R(1280, 0, 640, 1040), far.Frame);
    }

    [Fact]
    public void StackedColumnBesideAFullHeightWindowMovesBothWindows()
    {
        var area = Area();
        var active = W(1, R(960, 0, 960, 1040));
        var top = W(2, R(0, 0, 960, 520));
        var bottom = W(3, R(0, 520, 960, 520));

        var plan = EdgeResize.Keyboard(active, area, [top, bottom], WindowDirection.Left);

        Assert.Equal(R(864, 0, 1056, 1040), plan.Frames[active.Handle]);
        Assert.Equal(R(0, 0, 864, 520), plan.Frames[top.Handle]);
        Assert.Equal(R(0, 520, 864, 520), plan.Frames[bottom.Handle]);
    }

    [Fact]
    public void StackedColumnJoinsTransitivelyThroughTheSameSideMember()
    {
        var area = Area();
        var active = W(1, R(960, 400, 960, 200));
        var directlyTouching = W(2, R(0, 400, 960, 200));
        // Same edge on the line, but only abutting the first stacked window.
        var transitivelyTouching = W(3, R(0, 600, 960, 200));

        var plan = EdgeResize.Keyboard(active, area, [directlyTouching, transitivelyTouching], WindowDirection.Left);

        var line = Assert.Single(plan.Lines);
        Assert.Equal(3, line.Members.Count);
        Assert.Equal(R(864, 400, 1056, 200), plan.Frames[active.Handle]);
        Assert.Equal(R(0, 400, 864, 200), plan.Frames[directlyTouching.Handle]);
        Assert.Equal(R(0, 600, 864, 200), plan.Frames[transitivelyTouching.Handle]);
    }

    [Fact]
    public void CornerOnlyTouchIsNotANeighbour()
    {
        var area = Area();
        var active = W(1, R(960, 0, 960, 500));
        var corner = W(2, R(0, 500, 960, 100));

        var plan = EdgeResize.Keyboard(active, area, [corner], WindowDirection.Left);

        var line = Assert.Single(plan.Lines);
        Assert.Single(line.Members);
        Assert.True(plan.Frames.ContainsKey(active.Handle));
        Assert.False(plan.Frames.ContainsKey(corner.Handle));
    }

    [Fact]
    public void DisjointWindowsOnTheSameLineAreNotJoined()
    {
        var area = Area();
        var active = W(1, R(960, 0, 960, 400));
        var touching = W(2, R(0, 0, 960, 200));
        var disjoint = W(3, R(0, 700, 960, 100));

        var plan = EdgeResize.Keyboard(active, area, [touching, disjoint], WindowDirection.Left);

        Assert.True(plan.Frames.ContainsKey(touching.Handle));
        Assert.False(plan.Frames.ContainsKey(disjoint.Handle));
    }

    [Fact]
    public void PositiveFourPixelGapIsRetained()
    {
        var area = Area();
        var active = W(1, R(964, 0, 956, 1040));
        var neighbour = W(2, R(0, 0, 960, 1040));

        var plan = EdgeResize.Keyboard(active, area, [neighbour], WindowDirection.Left);

        var movedActive = plan.Frames[active.Handle];
        var movedNeighbour = plan.Frames[neighbour.Handle];
        Assert.Equal(R(868, 0, 1052, 1040), movedActive);
        Assert.Equal(R(0, 0, 864, 1040), movedNeighbour);
        Assert.Equal(4, movedActive.X - movedNeighbour.Right);
    }

    [Fact]
    public void SmallVisibleOverlapIsClosedToTouch()
    {
        var area = Area();
        var active = W(1, R(0, 0, 960, 1040));
        var overlapping = W(2, R(950, 0, 970, 1040)); // 10 px visible overlap on the shared line

        var plan = EdgeResize.Keyboard(active, area, [overlapping], WindowDirection.Right);

        var movedActive = plan.Frames[active.Handle];
        var movedNeighbour = plan.Frames[overlapping.Handle];
        Assert.Equal(1056, movedActive.Right);
        Assert.Equal(1056, movedNeighbour.X);
        Assert.Equal(0, movedActive.Right - movedNeighbour.X);
    }

    [Fact]
    public void VisibleFramesTouchWhenOuterRectsOverlapByFourteenPixels()
    {
        var area = Area();
        var left = W(1, R(0, 0, 960, 1040), insets: new FrameInsets(0, 0, 7, 7));
        var right = W(2, R(960, 0, 960, 1040), insets: new FrameInsets(7, 0, 0, 7));

        var plan = EdgeResize.Keyboard(left, area, [right], WindowDirection.Right);

        Assert.Equal(2, Assert.Single(plan.Lines).Members.Count);
        Assert.Equal(1056, plan.Frames[left.Handle].Right);
        Assert.Equal(1056, plan.Frames[right.Handle].X);
        var outer = plan.OuterRects;
        Assert.Equal(1063, outer[left.Handle].Right);
        Assert.Equal(1049, outer[right.Handle].X);
        Assert.Equal(14, outer[left.Handle].Right - outer[right.Handle].X);
    }

    [Fact]
    public void MinimumSizeStopsTheLineAtTheTightestWindow()
    {
        var area = R(0, 0, 2000, 1040);
        var active = W(1, R(0, 0, 1000, 1040));
        var tight = W(2, R(1000, 0, 450, 400), scale: 2); // 400 px scaled minimum leaves 50 px
        var looser = W(3, R(1000, 450, 1000, 400), scale: 2, minWidth: 800); // 200 px available

        var plan = EdgeResize.Keyboard(active, area, [tight, looser], WindowDirection.Right);

        Assert.Equal(50, Assert.Single(plan.Lines).Delta);
        Assert.Equal(R(0, 0, 1050, 1040), plan.Frames[active.Handle]);
        Assert.Equal(R(1050, 0, 400, 400), plan.Frames[tight.Handle]);
        Assert.Equal(R(1050, 450, 950, 400), plan.Frames[looser.Handle]);
    }

    [Fact]
    public void FixedWindowPinsTheLine()
    {
        var area = Area();
        var active = W(1, R(0, 0, 960, 1040));
        var obstacle = W(2, R(960, 0, 960, 1040), fixedWindow: true);

        var plan = EdgeResize.Keyboard(active, area, [obstacle], WindowDirection.Right);

        Assert.Equal(0, Assert.Single(plan.Lines).Delta);
        Assert.Empty(plan.Frames);
    }

    [Fact]
    public void BorderToleranceIsTwelvePixelsAndSnapsToTheBorder()
    {
        var area = Area();
        var atTolerance = W(1, R(0, 0, 1908, 1040)); // 12 px from the right border
        var pastTolerance = W(2, R(0, 0, 1907, 1040)); // 13 px away

        Assert.True(EdgeResize.OnBorder(atTolerance, area, WindowDirection.Right));
        Assert.False(EdgeResize.OnBorder(pastTolerance, area, WindowDirection.Right));

        var snapped = EdgeResize.Keyboard(atTolerance, area, [], WindowDirection.Right);
        Assert.Equal(WindowDirection.Left, Assert.Single(snapped.Lines).Edge);
        Assert.Equal(96, snapped.Lines[0].Delta);

        var free = EdgeResize.Keyboard(pastTolerance, area, [], WindowDirection.Right);
        Assert.Equal(WindowDirection.Right, Assert.Single(free.Lines).Edge);
        Assert.Equal(13, free.Lines[0].Delta); // the line stops at the work-area border
    }

    [Fact]
    public void PartlyOffscreenLeftArrowDoesNotReverseDirection()
    {
        var area = Area();
        var active = W(1, R(-50, 100, 500, 600)); // left edge is 50 px off-screen, beyond the 12 px tolerance

        var plan = EdgeResize.Keyboard(active, area, [], WindowDirection.Left);

        var line = Assert.Single(plan.Lines);
        Assert.Equal(WindowDirection.Left, line.Edge);
        Assert.Equal(0, line.Delta);
        Assert.Empty(plan.Frames);
    }

    [Fact]
    public void WindowOnAnotherMonitorIsIgnored()
    {
        var area = Area();
        var active = W(1, R(0, 0, 900, 1040));
        var above = W(2, R(900, -1040, 900, 1040)); // same x edge, but its centre is outside the work area

        var plan = EdgeResize.Keyboard(active, area, [above], WindowDirection.Right);

        Assert.True(plan.Frames.ContainsKey(active.Handle));
        Assert.False(plan.Frames.ContainsKey(above.Handle));
    }

    [Fact]
    public void ScaledDisplayUsesScaledToleranceAndMinimum()
    {
        var area = Area();
        var near = W(1, R(0, 0, 1900, 1040), scale: 2); // 20 px away; 2x tolerance is 24
        var far = W(2, R(0, 0, 1895, 1040), scale: 2); // 25 px away

        Assert.True(EdgeResize.OnBorder(near, area, WindowDirection.Right));
        Assert.False(EdgeResize.OnBorder(far, area, WindowDirection.Right));

        var smallArea = R(0, 0, 2000, 1040);
        var active = W(3, R(0, 0, 1000, 1040));
        var scaled = W(4, R(1000, 0, 450, 1040), scale: 2); // scaled minimum 400 -> only 50 px of travel
        var plan = EdgeResize.Keyboard(active, smallArea, [scaled], WindowDirection.Right);
        Assert.Equal(50, Assert.Single(plan.Lines).Delta);
        Assert.Equal(400, plan.Frames[scaled.Handle].Width);
    }

    [Fact]
    public void WheelWithANeighbourGrowsAndShrinksEveryFreeLine()
    {
        var area = Area();
        var active = W(1, R(0, 0, 960, 1040));
        var neighbour = W(2, R(960, 0, 960, 1040));

        var grow = EdgeResize.Wheel(active, area, [neighbour], true);
        var growLine = Assert.Single(grow.Lines);
        Assert.Equal(WindowDirection.Right, growLine.Edge);
        Assert.Equal(96, growLine.Delta);
        Assert.Equal(R(0, 0, 1056, 1040), grow.Frames[active.Handle]);
        Assert.Equal(R(1056, 0, 864, 1040), grow.Frames[neighbour.Handle]);

        var shrink = EdgeResize.Wheel(active, area, [neighbour], false);
        Assert.Equal(-96, Assert.Single(shrink.Lines).Delta);
        Assert.Equal(R(0, 0, 864, 1040), shrink.Frames[active.Handle]);
        Assert.Equal(R(864, 0, 1056, 1040), shrink.Frames[neighbour.Handle]);
    }

    [Fact]
    public void WheelWithoutNeighboursKeepsTheOldCentreResize()
    {
        var area = Area();
        var active = W(1, R(100, 100, 800, 600));

        var grow = EdgeResize.Wheel(active, area, [], true);

        Assert.Empty(grow.Lines);
        Assert.Equal(WindowGeometry.Resize(area, active.Frame, true), grow.Frames[active.Handle]);
        Assert.Equal(WindowGeometry.Resize(area, active.Frame, false),
            EdgeResize.Wheel(active, area, [], false).Frames[active.Handle]);
    }

    [Fact]
    public void WheelShrinkReducesBothFreeHorizontalEdgesJointlyToTheActiveMinimum()
    {
        var area = Area();
        var active = W(1, R(300, 0, 300, 1040));
        var left = W(2, R(0, 0, 300, 1040));
        var right = W(3, R(600, 0, 1320, 1040));

        var plan = EdgeResize.Wheel(active, area, [left, right], grow: false);

        var moved = plan.Frames[active.Handle];
        Assert.Equal(200, moved.Width);
        Assert.Equal(R(350, 0, 200, 1040), moved);
        Assert.Equal(R(0, 0, 350, 1040), plan.Frames[left.Handle]);
        Assert.Equal(R(550, 0, 1370, 1040), plan.Frames[right.Handle]);
        Assert.All(plan.Frames.Values, r => Assert.True(r.Width >= 200));
    }

    [Fact]
    public void IsolatedWheelClampIsAcceptedWithoutRollback()
    {
        var ops = new EdgeFakeWindowOps();
        long active = ops.AddVisible(R(100, 100, 800, 600));
        ops.Foreground = active;
        var actions = new WindowShortcutActions(ops, null, new RecordingDelay());
        ops.Transform = (handle, rect, _) => handle == active
            ? new PixelRect(rect.X, rect.Y, 850, rect.Height)
            : rect;

        actions.ResizeWindow(grow: true, CancellationToken.None);

        Assert.Equal(850, ops.Edges[active].Frame.Width);
        Assert.Single(ops.DirectMoves);
        Assert.Empty(ops.Batches);
    }

    [Fact]
    public void FixedIsolatedWheelIsANoOp()
    {
        var area = Area();
        var active = W(1, R(100, 100, 800, 600), fixedWindow: true);

        var plan = EdgeResize.Wheel(active, area, [], grow: true);

        Assert.Empty(plan.Lines);
        Assert.Empty(plan.Windows);
        Assert.Empty(plan.Frames);
    }

    [Fact]
    public void ResizeTogetherOffMovesOnlyTheActiveWindowAndWheelUsesTheOldCentreResize()
    {
        var ops = new EdgeFakeWindowOps();
        long active = ops.AddVisible(R(0, 0, 960, 1040));
        long neighbour = ops.AddVisible(R(960, 0, 960, 1040));
        ops.Foreground = active;
        var actions = new WindowShortcutActions(ops, null, new RecordingDelay());
        actions.ResizeTouchingWindowsTogether = false;

        actions.Execute(HotkeyCatalog.Find("edge-resize-right")!, CancellationToken.None);

        Assert.Equal(R(0, 0, 1056, 1040), ops.Edges[active].Frame);
        Assert.Equal(R(960, 0, 960, 1040), ops.Edges[neighbour].Frame);

        var beforeWheel = ops.Edges[active].Frame;
        actions.ResizeWindow(grow: true, CancellationToken.None);

        Assert.Equal(WindowGeometry.Resize(Area(), beforeWheel, true), ops.Edges[active].Frame);
        Assert.Equal(R(960, 0, 960, 1040), ops.Edges[neighbour].Frame);
    }

    [Fact]
    public void ApplyMovesShrinkingWindowsBeforeGrowingOnes()
    {
        var ops = new EdgeFakeWindowOps();
        long growHandle = ops.AddVisible(R(0, 0, 500, 500));
        long shrinkHandle = ops.AddVisible(R(600, 0, 500, 500));
        var grow = W(growHandle, R(0, 0, 500, 500));
        var shrink = W(shrinkHandle, R(600, 0, 500, 500));
        var plan = EdgeResize.FromLines(
        [
            new EdgeLine(WindowDirection.Right, 500, 100, [new EdgeMember(grow, WindowDirection.Right, 500)]),
            new EdgeLine(WindowDirection.Left, 600, 100, [new EdgeMember(shrink, WindowDirection.Left, 600)]),
        ]);

        Assert.True(EdgeResizeApplication.Apply(ops, plan));

        var batch = Assert.Single(ops.Batches);
        Assert.Equal(2, batch.Count);
        Assert.Equal(shrinkHandle, batch[0].Handle);
        Assert.Equal(growHandle, batch[1].Handle);
        Assert.Equal(R(700, 0, 400, 500), ops.Edges[shrinkHandle].Frame);
        Assert.Equal(R(0, 0, 600, 500), ops.Edges[growHandle].Frame);
    }

    [Fact]
    public void ClampedWindowCausesExactlyOneVerificationCorrection()
    {
        var (ops, active, neighbour, plan) = ClampScenario();
        // The neighbour cannot move further right than x=1020; the first batch asks for 1056.
        ops.Transform = (handle, rect, _) => handle == neighbour ? ClampLeft(rect, 1020) : rect;

        Assert.True(EdgeResizeApplication.Apply(ops, plan));

        Assert.Equal(2, ops.Batches.Count);
        Assert.Equal(R(0, 0, 1020, 1040), ops.Edges[active].Frame);
        Assert.Equal(R(1020, 0, 900, 1040), ops.Edges[neighbour].Frame);
    }

    [Fact]
    public void GridRoundedOvershootIsCorrectedOnceToTheActualEdge()
    {
        var (ops, active, neighbour, plan) = ClampScenario();
        // The app rounds the requested x=1056 to its character grid at x=1060.
        ops.Transform = (handle, rect, _) => handle == neighbour && rect.X == 1056
            ? new PixelRect(1060, rect.Y, rect.Width - 4, rect.Height)
            : rect;

        Assert.True(EdgeResizeApplication.Apply(ops, plan));

        Assert.Equal(2, ops.Batches.Count);
        Assert.Equal(R(0, 0, 1060, 1040), ops.Edges[active].Frame);
        Assert.Equal(R(1060, 0, 860, 1040), ops.Edges[neighbour].Frame);
    }

    [Fact]
    public void OvershootBelowMinimumDeltaRollsBack()
    {
        var (ops, plan, handle) = DeltaRangeScenario(minimum: 20, maximum: 100);
        ops.Transform = (_, rect, _) => rect.Right == 650
            ? new PixelRect(rect.X, rect.Y, rect.Width - 40, rect.Height) // achieved 10, below the 20 minimum
            : rect;

        Assert.False(EdgeResizeApplication.Apply(ops, plan));

        Assert.Equal(2, ops.Batches.Count);
        Assert.Equal(R(100, 100, 500, 500), ops.Edges[handle].Frame);
    }

    [Fact]
    public void OvershootAboveMaximumDeltaRollsBack()
    {
        var (ops, plan, handle) = DeltaRangeScenario(minimum: 0, maximum: 40);
        ops.Transform = (_, rect, _) => rect.Right == 650
            ? new PixelRect(rect.X, rect.Y, rect.Width + 10, rect.Height) // achieved 60, above the 40 maximum
            : rect;

        Assert.False(EdgeResizeApplication.Apply(ops, plan));

        Assert.Equal(2, ops.Batches.Count);
        Assert.Equal(R(100, 100, 500, 500), ops.Edges[handle].Frame);
    }

    [Fact]
    public void RefusingWindowRollsBackTheWholeTransaction()
    {
        var (ops, active, neighbour, plan) = ClampScenario();
        ops.Refusing.Add(neighbour);
        var activeStart = ops.Edges[active].Frame;
        var neighbourStart = ops.Edges[neighbour].Frame;

        Assert.False(EdgeResizeApplication.Apply(ops, plan));

        Assert.Equal(2, ops.Batches.Count);
        Assert.Equal(activeStart, ops.Edges[active].Frame);
        Assert.Equal(neighbourStart, ops.Edges[neighbour].Frame);
    }

    [Fact]
    public void FailedCorrectionRollsBackTheWholeTransaction()
    {
        var (ops, active, neighbour, plan) = ClampScenario();
        ops.Transform = (handle, rect, batch) => handle != neighbour ? rect
            : batch == 0 ? ClampLeft(rect, 1020)
            : batch == 1 ? new PixelRect(rect.X + 10, rect.Y, rect.Width, rect.Height)
            : rect;
        var activeStart = ops.Edges[active].Frame;
        var neighbourStart = ops.Edges[neighbour].Frame;

        Assert.False(EdgeResizeApplication.Apply(ops, plan));

        Assert.Equal(3, ops.Batches.Count);
        Assert.Equal(activeStart, ops.Edges[active].Frame);
        Assert.Equal(neighbourStart, ops.Edges[neighbour].Frame);
    }

    [Fact]
    public void ApplyClearsSnapForEveryTouchedHandle()
    {
        var (ops, active, neighbour, plan) = ClampScenario();
        var cleared = new List<long>();

        Assert.True(EdgeResizeApplication.Apply(ops, plan, cleared.Add));

        Assert.Equal(2, cleared.Count);
        Assert.Equal(plan.Frames.Keys.OrderBy(h => h), cleared.OrderBy(h => h));
    }

    [Fact]
    public void ApplyPreflightSkipsWhenAPlannedFrameBecameStale()
    {
        var (ops, _, neighbour, plan) = ClampScenario();
        ops.Move(neighbour, R(1100, 0, 820, 1040));
        var cleared = new List<long>();

        Assert.False(EdgeResizeApplication.Apply(ops, plan, cleared.Add));

        Assert.Empty(ops.Batches);
        Assert.Empty(cleared);
    }

    [Fact]
    public void ApplyPreflightSkipsWhenTheWorkAreaChanged()
    {
        var (ops, active, _, plan) = ClampScenario();
        ops.SetWorkArea(active, R(0, 0, 1000, 1040));
        var cleared = new List<long>();

        Assert.False(EdgeResizeApplication.Apply(ops, plan, cleared.Add, Area()));

        Assert.Empty(ops.Batches);
        Assert.Empty(cleared);
    }

    [Fact]
    public void ApplyPreflightSkipsWhenAFixedStateChanged()
    {
        var (ops, _, neighbour, plan) = ClampScenario();
        ops.Edges[neighbour] = ops.Edges[neighbour] with { Fixed = true };
        var cleared = new List<long>();

        Assert.False(EdgeResizeApplication.Apply(ops, plan, cleared.Add));

        Assert.Empty(ops.Batches);
        Assert.Empty(cleared);
    }

    [Fact]
    public void DragEdgeDetectsOnlySingleEdgeChanges()
    {
        var before = R(100, 100, 400, 300);
        Assert.Equal(WindowDirection.Left, EdgeResize.DragEdge(before, R(90, 100, 410, 300)));
        Assert.Equal(WindowDirection.Right, EdgeResize.DragEdge(before, R(100, 100, 390, 300)));
        Assert.Equal(WindowDirection.Up, EdgeResize.DragEdge(before, R(100, 90, 400, 310)));
        Assert.Equal(WindowDirection.Down, EdgeResize.DragEdge(before, R(100, 100, 400, 290)));
        Assert.Null(EdgeResize.DragEdge(before, R(90, 110, 400, 300))); // move
        Assert.Null(EdgeResize.DragEdge(before, R(90, 100, 390, 300))); // corner
    }

    [Fact]
    public void EndResizeDragMovesTheSnapshotNeighbourToTheNewLine()
    {
        var (ops, actions, active, neighbour) = DragScenario();
        Assert.NotNull(actions.BeginResizeDrag(active));
        var snapshot = actions.BeginResizeDrag(active)!;
        ops.Move(active, R(0, 0, 1056, 1040));

        actions.EndResizeDrag(snapshot);

        Assert.Equal(R(1056, 0, 864, 1040), ops.Edges[neighbour].Frame);
        Assert.Equal(R(0, 0, 1056, 1040), ops.Edges[active].Frame);
        Assert.Single(ops.Batches);
    }

    [Fact]
    public void DelayedBeginResizeDragSnapshotsTheEventStartFrame()
    {
        var (ops, actions, active, neighbour) = DragScenario();
        var eventStart = R(0, 0, 960, 1040);
        ops.Move(active, R(0, 0, 1056, 1040)); // active already resized before the delayed snapshot

        var snapshot = actions.BeginResizeDrag(active, eventStart);

        Assert.NotNull(snapshot);
        Assert.Equal(eventStart, snapshot!.Active.Frame);
        actions.EndResizeDrag(snapshot);
        Assert.Equal(R(1056, 0, 864, 1040), ops.Edges[neighbour].Frame);
        Assert.Single(ops.Batches);
    }

    [Fact]
    public void EndResizeDragReturnsTheActiveWindowToAFixedNeighboursPinnedEdge()
    {
        var ops = new EdgeFakeWindowOps();
        long active = ops.AddVisible(R(0, 0, 900, 1040));
        long pinned = ops.AddVisible(R(900, 0, 1020, 1040), fixedWindow: true);
        ops.Foreground = active;
        var actions = new WindowShortcutActions(ops, null, new RecordingDelay());
        var snapshot = actions.BeginResizeDrag(active)!;
        ops.Move(active, R(0, 0, 1056, 1040));
        ops.Batches.Clear();

        actions.EndResizeDrag(snapshot);

        Assert.Equal(R(0, 0, 900, 1040), ops.Edges[active].Frame);
        Assert.Equal(R(900, 0, 1020, 1040), ops.Edges[pinned].Frame);
        Assert.Single(ops.Batches);
    }

    [Fact]
    public void EndResizeDragAbortsWhenTheActiveDpiOrInsetsChanged()
    {
        var (scaleOps, scaleActions, scaleActive, _) = DragScenario();
        var scaleSnapshot = scaleActions.BeginResizeDrag(scaleActive)!;
        scaleOps.Move(scaleActive, R(0, 0, 1056, 1040));
        scaleOps.Edges[scaleActive] = scaleOps.Edges[scaleActive] with { Scale = 2 };
        scaleOps.Batches.Clear();
        scaleActions.EndResizeDrag(scaleSnapshot);
        Assert.Empty(scaleOps.Batches);

        var (insetOps, insetActions, insetActive, _) = DragScenario();
        var insetSnapshot = insetActions.BeginResizeDrag(insetActive)!;
        insetOps.Move(insetActive, R(0, 0, 1056, 1040));
        insetOps.Edges[insetActive] = insetOps.Edges[insetActive] with { Insets = new FrameInsets(7, 0, 7, 7) };
        insetOps.Batches.Clear();
        insetActions.EndResizeDrag(insetSnapshot);
        Assert.Empty(insetOps.Batches);
    }

    [Fact]
    public void EndResizeDragAbortsAStaleSnapshotWhenANeighbourDisappeared()
    {
        var (ops, actions, active, neighbour) = DragScenario();
        var snapshot = actions.BeginResizeDrag(active)!;
        ops.Remove(neighbour);
        ops.Move(active, R(0, 0, 1056, 1040));
        ops.Batches.Clear();

        actions.EndResizeDrag(snapshot);

        Assert.Empty(ops.Batches);
    }

    [Fact]
    public void EndResizeDragAbortsAStaleSnapshotWhenANeighbourWasMinimized()
    {
        var (ops, actions, active, neighbour) = DragScenario();
        var snapshot = actions.BeginResizeDrag(active)!;
        ops.Minimize(neighbour);
        ops.Move(active, R(0, 0, 1056, 1040));
        ops.Batches.Clear();

        actions.EndResizeDrag(snapshot);

        Assert.Empty(ops.Batches);
    }

    [Fact]
    public void EndResizeDragSkipsWhenTheSnapGroupAlreadyFollowed()
    {
        var (ops, actions, active, neighbour) = DragScenario();
        var snapshot = actions.BeginResizeDrag(active)!;
        ops.Move(active, R(0, 0, 1056, 1040));
        ops.Move(neighbour, R(1056, 0, 864, 1040));
        ops.Batches.Clear();

        actions.EndResizeDrag(snapshot);

        Assert.Equal(R(1056, 0, 864, 1040), ops.Edges[neighbour].Frame);
        Assert.Empty(ops.Batches);
    }

    [Fact]
    public void EndResizeDragAbortsWhenANeighbourMovedSomewhereElse()
    {
        var (ops, actions, active, neighbour) = DragScenario();
        var snapshot = actions.BeginResizeDrag(active)!;
        ops.Move(active, R(0, 0, 1056, 1040));
        ops.Move(neighbour, R(1100, 0, 820, 1040));
        ops.Batches.Clear();

        actions.EndResizeDrag(snapshot);

        Assert.Empty(ops.Batches);
    }

    [Fact]
    public void EndResizeDragClampsTheActiveEdgeToTheSharedMinimum()
    {
        var ops = new EdgeFakeWindowOps();
        long active = ops.AddVisible(R(0, 0, 900, 1040));
        long neighbour = ops.AddVisible(R(900, 0, 300, 1040));
        ops.Foreground = active;
        var actions = new WindowShortcutActions(ops, null, new RecordingDelay());
        var snapshot = actions.BeginResizeDrag(active)!;
        ops.Move(active, R(0, 0, 1056, 1040)); // user drags 156 px, neighbour only allows 100

        actions.EndResizeDrag(snapshot);

        Assert.Equal(R(0, 0, 1000, 1040), ops.Edges[active].Frame);
        Assert.Equal(R(1000, 0, 200, 1040), ops.Edges[neighbour].Frame);
        Assert.Single(ops.Batches);
    }

    [Fact]
    public void EndResizeDragRollsBackTheActiveWindowWhenANeighbourRefuses()
    {
        var ops = new EdgeFakeWindowOps();
        long active = ops.AddVisible(R(0, 0, 900, 1040));
        long neighbour = ops.AddVisible(R(900, 0, 300, 1040));
        ops.Foreground = active;
        var actions = new WindowShortcutActions(ops, null, new RecordingDelay());
        var snapshot = actions.BeginResizeDrag(active)!;
        ops.Move(active, R(0, 0, 1056, 1040));
        ops.Refusing.Add(neighbour);
        ops.Batches.Clear();

        actions.EndResizeDrag(snapshot);

        Assert.Equal(R(0, 0, 900, 1040), ops.Edges[active].Frame);
        Assert.Equal(R(900, 0, 300, 1040), ops.Edges[neighbour].Frame);
        Assert.Equal(2, ops.Batches.Count);
    }

    [Fact]
    public void ActionCandidatesIgnoreCloakedMinimizedMaximizedToolAndShellWindows()
    {
        var ops = new EdgeFakeWindowOps();
        long active = ops.AddVisible(R(0, 0, 960, 1040));
        long valid = ops.AddVisible(R(960, 0, 960, 1040));
        long cloaked = ops.AddVisible(R(960, 0, 960, 1040), cloaked: true);
        long minimized = ops.AddVisible(R(960, 0, 960, 1040), minimized: true);
        long maximized = ops.AddVisible(R(960, 0, 960, 1040), maximized: true);
        long tool = ops.AddVisible(R(960, 0, 960, 1040), tool: true);
        long shell = ops.AddVisible(R(960, 0, 960, 1040), cls: "Progman");
        long invisible = ops.AddVisible(R(960, 0, 960, 1040), visibleWindow: false);
        long untitled = ops.AddVisible(R(960, 0, 960, 1040), title: "");
        ops.Foreground = active;
        var actions = new WindowShortcutActions(ops, null, new RecordingDelay());

        actions.Execute(HotkeyCatalog.Find("edge-resize-right")!, CancellationToken.None);

        Assert.Equal(R(1056, 0, 864, 1040), ops.Edges[valid].Frame);
        foreach (long ignored in new[] { cloaked, minimized, maximized, tool, shell, invisible, untitled })
            Assert.Equal(R(960, 0, 960, 1040), ops.Edges[ignored].Frame);
        Assert.Equal(2, Assert.Single(ops.Batches).Count);
    }

    [Fact]
    public void OwnForegroundWindowIsSkipped()
    {
        var ops = new EdgeFakeWindowOps();
        long active = ops.AddVisible(R(0, 0, 960, 1040));
        ops.AddVisible(R(960, 0, 960, 1040));
        ops.Foreground = active;
        ops.OwnHandles.Add(active);
        var actions = new WindowShortcutActions(ops, null, new RecordingDelay());

        actions.Execute(HotkeyCatalog.Find("edge-resize-right")!, CancellationToken.None);
        actions.ResizeWindow(grow: true, CancellationToken.None);

        Assert.Empty(ops.Batches);
        Assert.Empty(ops.DirectMoves);
    }

    [Fact]
    public void ShellForegroundAndPauseBlockEdgeResizeAndDrag()
    {
        var ops = new EdgeFakeWindowOps();
        long active = ops.AddVisible(R(0, 0, 960, 1040));
        long neighbour = ops.AddVisible(R(960, 0, 960, 1040));
        ops.Foreground = active;
        var actions = new WindowShortcutActions(ops, null, new RecordingDelay());

        actions.SetPaused(true);
        actions.Execute(HotkeyCatalog.Find("edge-resize-right")!, CancellationToken.None);
        Assert.Null(actions.BeginResizeDrag(active));
        Assert.Empty(ops.Batches);

        actions.SetPaused(false);
        ops.Foreground = neighbour;
        ops.ShellHandles.Add(neighbour);
        actions.Execute(HotkeyCatalog.Find("edge-resize-left")!, CancellationToken.None);
        Assert.Empty(ops.Batches);
    }

    private static (EdgeFakeWindowOps Ops, long Active, long Neighbour, EdgeResizePlan Plan) ClampScenario()
    {
        var ops = new EdgeFakeWindowOps();
        long active = ops.AddVisible(R(0, 0, 960, 1040));
        long neighbour = ops.AddVisible(R(960, 0, 960, 1040));
        var plan = EdgeResize.Keyboard(ops.Edges[active], Area(), [ops.Edges[neighbour]], WindowDirection.Right);
        return (ops, active, neighbour, plan);
    }

    private static (EdgeFakeWindowOps Ops, EdgeResizePlan Plan, long Handle) DeltaRangeScenario(int minimum, int maximum)
    {
        var ops = new EdgeFakeWindowOps();
        long handle = ops.AddVisible(R(100, 100, 500, 500));
        var window = W(handle, R(100, 100, 500, 500));
        var line = new EdgeLine(WindowDirection.Right, 600, 50,
            [new EdgeMember(window, WindowDirection.Right, 600)], minimum, maximum);
        return (ops, EdgeResize.FromLines([line]), handle);
    }

    private static (EdgeFakeWindowOps Ops, WindowShortcutActions Actions, long Active, long Neighbour) DragScenario()
    {
        var ops = new EdgeFakeWindowOps();
        long active = ops.AddVisible(R(0, 0, 960, 1040));
        long neighbour = ops.AddVisible(R(960, 0, 960, 1040));
        ops.Foreground = active;
        return (ops, new WindowShortcutActions(ops, null, new RecordingDelay()), active, neighbour);
    }

    private static PixelRect ClampLeft(PixelRect r, int maxX)
    {
        int x = Math.Min(r.X, maxX);
        return new PixelRect(x, r.Y, r.Width + (r.X - x), r.Height);
    }
}

/// <summary>IWindowOps fake with visible frames, outer insets, per-window minima and move scripting.</summary>
internal sealed class EdgeFakeWindowOps : IWindowOps
{
    private long _next = 100;
    private int _batch = -1;

    public long Foreground { get; set; }
    public Dictionary<long, WindowMetrics> Metrics { get; } = [];
    public Dictionary<long, EdgeWindow> Edges { get; } = [];
    public List<FocusWindow> FocusWindows { get; } = [];
    public List<List<(long Handle, PixelRect Rect)>> Batches { get; } = [];
    public List<(long Handle, PixelRect Rect)> DirectMoves { get; } = [];
    public HashSet<long> Refusing { get; } = [];
    public HashSet<long> ShellHandles { get; } = [];
    public HashSet<long> OwnHandles { get; } = [];
    public Func<long, PixelRect, int, PixelRect>? Transform { get; set; }

    public long AddVisible(PixelRect visible, FrameInsets insets = default, PixelRect? workArea = null,
        double scale = 1, int minWidth = 0, int minHeight = 0, bool fixedWindow = false,
        bool visibleWindow = true, bool cloaked = false, bool minimized = false, bool maximized = false,
        string title = "Window", string cls = "Win", bool tool = false, bool shell = false)
    {
        long handle = _next++;
        var outer = insets.Outer(visible);
        Metrics[handle] = new WindowMetrics(outer, workArea ?? new PixelRect(0, 0, 1920, 1040),
            minimized, maximized, true);
        Edges[handle] = new EdgeWindow(handle, visible, insets, scale, minWidth, minHeight, fixedWindow);
        FocusWindows.Add(new FocusWindow(handle, outer, FocusWindows.Count, visibleWindow, cloaked,
            minimized, title, cls, tool));
        if (shell) ShellHandles.Add(handle);
        return handle;
    }

    public void Remove(long handle)
    {
        Metrics.Remove(handle);
        Edges.Remove(handle);
        FocusWindows.RemoveAll(w => w.Handle == handle);
    }

    public void SetWorkArea(long handle, PixelRect area)
    {
        if (Metrics.TryGetValue(handle, out var metrics)) Metrics[handle] = metrics with { WorkArea = area };
    }

    public long GetForeground() => Foreground;
    public bool IsShellWindow(long handle) => ShellHandles.Contains(handle);
    public bool IsOwnWindow(long handle) => OwnHandles.Contains(handle);
    public WindowMetrics GetMetrics(long handle) => Metrics.TryGetValue(handle, out var metrics) ? metrics : default;

    public EdgeWindow? GetEdgeWindow(long handle) =>
        Metrics.TryGetValue(handle, out var metrics) && metrics.Exists && !metrics.IsMaximized && !metrics.IsMinimized &&
        Edges.TryGetValue(handle, out var edge) ? edge : null;

    public bool MoveBatch(IReadOnlyList<KeyValuePair<long, PixelRect>> moves)
    {
        int index = Batches.Count;
        var log = new List<(long Handle, PixelRect Rect)>();
        _batch = index;
        bool success = true;
        foreach (var move in moves)
        {
            success &= Move(move.Key, move.Value);
            log.Add((move.Key, Edges.TryGetValue(move.Key, out var edge) ? edge.Insets.Outer(edge.Frame) : move.Value));
        }
        Batches.Add(log);
        _batch = -1;
        return success;
    }

    public bool Move(long handle, PixelRect rect)
    {
        if (Transform is not null) rect = Transform(handle, rect, _batch);
        if (_batch < 0) DirectMoves.Add((handle, rect));
        if (Refusing.Contains(handle) || !Metrics.TryGetValue(handle, out var metrics)) return false;
        Edges.TryGetValue(handle, out var edge);
        var insets = edge?.Insets ?? default;
        var visible = PixelRect.FromLtrb(rect.X + insets.Left, rect.Y + insets.Top,
            rect.Right - insets.Right, rect.Bottom - insets.Bottom);
        Metrics[handle] = metrics with { Rect = rect };
        if (edge is not null) Edges[handle] = edge with { Frame = visible };
        int focusIndex = FocusWindows.FindIndex(w => w.Handle == handle);
        if (focusIndex >= 0) FocusWindows[focusIndex] = FocusWindows[focusIndex] with { Rect = rect };
        return true;
    }

    public void Restore(long handle)
    {
        if (!Metrics.TryGetValue(handle, out var metrics)) return;
        Metrics[handle] = metrics with { IsMinimized = false, IsMaximized = false };
    }

    public void Maximize(long handle)
    {
        if (Metrics.TryGetValue(handle, out var metrics)) Metrics[handle] = metrics with { IsMaximized = true };
    }

    public void Minimize(long handle)
    {
        if (Metrics.TryGetValue(handle, out var metrics)) Metrics[handle] = metrics with { IsMinimized = true };
    }

    public bool Activate(long handle)
    {
        Foreground = handle;
        return true;
    }

    public IReadOnlyList<FocusWindow> GetWindowsInZOrder() => FocusWindows.ToList();
}
