using FlowDesk.Core.Layout;
using FlowDesk.Core.Model;
using FlowDesk.Core.Windows;

namespace FlowDesk.Core.Tests;

/// <summary>
/// Model-based tests for the extracted placement algorithm. The fake follows the documented SHOW command
/// categories (SHOWNOACTIVATE behaves like SHOWNORMAL without activation); the opt-in
/// <see cref="FakePlacementApi.IgnoreNonActivatingShow"/> flag is fault injection for a hosted window ignoring
/// that request, not a claim about the actual Calculator mechanism.
/// </summary>
public class WindowPlacerTests
{
    private const int ShowNormal = 1;       // SW_SHOWNORMAL
    private const int ShowMaximized = 3;    // SW_SHOWMAXIMIZED
    private const int ShowNoActivate = 4;   // SW_SHOWNOACTIVATE
    private const int ShowNA = 8;           // SW_SHOWNA
    private const int ShowMinNoActivate = 7; // SW_SHOWMINNOACTIVE
    private const int ShowRestore = 9;      // SW_RESTORE

    private static readonly PixelRect Target = new(720, 120, 880, 625);

    [Fact]
    public void Minimized_previous_max_paint_is_normalized_by_show4_before_moving_and_maximized()
    {
        var api = new FakePlacementApi { Iconic = true, RestoreWasMaximized = true };

        var outcome = WindowPlacer.Place(api, Target, WindowShow.Maximized);

        Assert.Equal(PlaceOutcome.Ok, outcome);
        Assert.False(api.Iconic);
        Assert.True(api.Maximized);
        Assert.Equal(Target, api.RestoreRect);
        AssertShowBeforeFirstMove(api, ShowNoActivate);
        Assert.DoesNotContain("Show9", api.Ops);
        Assert.DoesNotContain("Show1", api.Ops);
    }

    [Fact]
    public void Ignored_non_activating_show_recovers_with_explicit_restore_for_an_iconic_window()
    {
        var api = new FakePlacementApi { Iconic = true, IgnoreNonActivatingShow = true };

        var outcome = WindowPlacer.Place(api, Target, WindowShow.Normal);

        Assert.Equal(PlaceOutcome.Ok, outcome);
        Assert.False(api.Iconic);
        Assert.False(api.Maximized);
        Assert.Equal(Target, api.RestoreRect);
        AssertShowBeforeFirstMove(api, ShowRestore);
        Assert.True(api.Ops.IndexOf("Show4") < api.Ops.IndexOf("Show9"));
    }

    [Fact]
    public void Maximized_window_is_normalized_by_show4_before_moving()
    {
        var api = new FakePlacementApi { Maximized = true };

        var outcome = WindowPlacer.Place(api, Target, WindowShow.Normal);

        Assert.Equal(PlaceOutcome.Ok, outcome);
        Assert.False(api.Iconic);
        Assert.False(api.Maximized);
        Assert.Equal(Target, api.RestoreRect);
        AssertShowBeforeFirstMove(api, ShowNoActivate);
        Assert.DoesNotContain("Show1", api.Ops);
    }

    [Fact]
    public void Ignored_non_activating_show_recovery_normalizes_a_remaining_maximized_window()
    {
        var api = new FakePlacementApi { Maximized = true, IgnoreNonActivatingShow = true };

        var outcome = WindowPlacer.Place(api, Target, WindowShow.Normal);

        Assert.Equal(PlaceOutcome.Ok, outcome);
        Assert.False(api.Iconic);
        Assert.False(api.Maximized);
        Assert.Equal(Target, api.RestoreRect);
        AssertShowBeforeFirstMove(api, ShowNormal);
        Assert.True(api.Ops.IndexOf("Show4") < api.Ops.IndexOf("Show1"));
    }

    [Fact]
    public void Normal_window_going_to_normal_is_only_moved()
    {
        var api = new FakePlacementApi();

        var outcome = WindowPlacer.Place(api, Target, WindowShow.Normal);

        Assert.Equal(PlaceOutcome.Ok, outcome);
        Assert.Equal(Target, api.RestoreRect);
        Assert.DoesNotContain(api.Ops, o => o.StartsWith("Show"));
        Assert.Single(api.RectMoves());
    }

    [Fact]
    public void Iconic_window_with_maximized_flag_on_target_monitor_must_not_skip_as_already_placed()
    {
        // Minimized+maximized can both report true for the same window. The existing-window fast path must
        // not treat that as "already placed": the window is iconic and still needs a move.
        var api = new FakePlacementApi { Iconic = true, Maximized = true, OnTargetMonitor = true };

        var outcome = WindowPlacer.Place(api, Target, WindowShow.Maximized);

        Assert.Equal(PlaceOutcome.Ok, outcome);
        Assert.False(api.Iconic);
        Assert.True(api.Maximized);
        Assert.Equal(Target, api.RestoreRect);
        Assert.NotEmpty(api.RectMoves());
        AssertShowBeforeFirstMove(api, ShowNoActivate);
        Assert.DoesNotContain("Show9", api.Ops);
        Assert.DoesNotContain("Show1", api.Ops);
    }

    [Fact]
    public void Visible_maximized_window_on_target_monitor_stays_a_no_op()
    {
        var api = new FakePlacementApi { Maximized = true, OnTargetMonitor = true };

        var outcome = WindowPlacer.Place(api, Target, WindowShow.Maximized);

        Assert.Equal(PlaceOutcome.Ok, outcome);
        Assert.Empty(api.Ops);
    }

    [Fact]
    public void Already_minimized_window_targeting_minimized_stays_a_no_op()
    {
        var api = new FakePlacementApi { Iconic = true };

        var outcome = WindowPlacer.Place(api, Target, WindowShow.Minimized);

        Assert.Equal(PlaceOutcome.Ok, outcome);
        Assert.True(api.Iconic);
        Assert.Empty(api.Ops);
    }

    [Fact]
    public void Missing_window_reports_window_gone_without_touching_it()
    {
        var api = new FakePlacementApi { WindowExists = false };

        var outcome = WindowPlacer.Place(api, Target, WindowShow.Normal);

        Assert.Equal(PlaceOutcome.WindowGone, outcome);
        Assert.Empty(api.Ops);
    }

    [Fact]
    public void Hung_window_is_placed_without_a_synchronous_show_and_does_not_block()
    {
        // Regardless of whether the implementation uses an asynchronous move or an asynchronous placement,
        // it must never call the synchronous ShowWindow family on a hung window and must not block.
        var api = new FakePlacementApi { Hung = true };

        var outcome = WindowPlacer.Place(api, Target, WindowShow.Maximized);

        Assert.Equal(PlaceOutcome.Ok, outcome);
        Assert.Equal(Target, api.LastRequestedRect);
        Assert.DoesNotContain(api.Ops, o => o.StartsWith("Show"));
    }

    [Fact]
    public void Hung_iconic_window_targeting_normal_must_unminimize()
    {
        // Regression for Calculator: a hung hosted frame used to get only an asynchronous rect move,
        // so the requested show state was silently ignored and the window stayed iconic.
        var api = new FakePlacementApi { Hung = true, Iconic = true };

        var outcome = WindowPlacer.Place(api, Target, WindowShow.Normal);

        Assert.Equal(PlaceOutcome.Ok, outcome);
        Assert.False(api.Iconic);
        Assert.False(api.Maximized);
        Assert.Equal(Target, api.RestoreRect);
    }

    [Fact]
    public void Hung_iconic_window_targeting_maximized_must_apply_the_show_state()
    {
        var api = new FakePlacementApi { Hung = true, Iconic = true };

        var outcome = WindowPlacer.Place(api, Target, WindowShow.Maximized);

        Assert.Equal(PlaceOutcome.Ok, outcome);
        Assert.False(api.Iconic);
        Assert.True(api.Maximized);
        Assert.Equal(Target, api.RestoreRect);
    }

    [Fact]
    public void Layout_applies_a_hung_minimized_window_through_the_same_placement_path()
    {
        var api = new FakePlacementApi { Hung = true, Iconic = true };
        var window = new WindowInfo
        {
            Handle = 42, ProcessId = 7164, ExePath = @"C:\Windows\System32\ApplicationFrameHost.exe",
            Title = "Calculator", ClassName = "ApplicationFrameWindow", NormalRect = new(1400, 120, 418, 635),
            Show = WindowShow.Minimized, MonitorDevice = "\\\\.\\DISPLAY1",
        };
        var sys = new PlacerWindowSystem(api, window);

        var result = new LayoutApplier(sys).Apply(LayoutPreset.TwoColumns, [window]);

        Assert.Equal(PlaceOutcome.Ok, Assert.Single(result).Outcome);
        Assert.False(api.Iconic);
        Assert.Contains(api.Ops, o => o.StartsWith("SetPlacement:"));
    }

    [Fact]
    public void Access_denied_from_the_native_move_is_reported()
    {
        var api = new FakePlacementApi { MoveSucceeds = false, LastError = 5 };

        var outcome = WindowPlacer.Place(api, Target, WindowShow.Normal);

        Assert.Equal(PlaceOutcome.AccessDenied, outcome);
    }

    [Fact]
    public void Other_native_move_failure_is_reported_as_failed()
    {
        var api = new FakePlacementApi { MoveSucceeds = false, LastError = 87 };

        var outcome = WindowPlacer.Place(api, Target, WindowShow.Normal);

        Assert.Equal(PlaceOutcome.Failed, outcome);
    }

    [Fact]
    public void Hung_async_placement_denied_by_the_os_is_reported_as_access_denied()
    {
        var api = new FakePlacementApi { Hung = true, PlacementFails = true, LastError = 5 };

        var outcome = WindowPlacer.Place(api, Target, WindowShow.Normal);

        Assert.Equal(PlaceOutcome.AccessDenied, outcome);
        Assert.Equal(0, api.Pauses);
    }

    [Fact]
    public void Hung_async_placement_other_failure_is_reported_as_failed()
    {
        var api = new FakePlacementApi { Hung = true, PlacementFails = true, LastError = 87 };

        var outcome = WindowPlacer.Place(api, Target, WindowShow.Normal);

        Assert.Equal(PlaceOutcome.Failed, outcome);
        Assert.Contains("SetPlacement:Normal", api.Ops);
        Assert.Equal(0, api.Pauses);
    }

    [Fact]
    public void Hung_window_that_disappears_during_bounded_polling_reports_window_gone()
    {
        var api = new FakePlacementApi { Hung = true, Iconic = true, IgnorePlacementState = true, VanishAfterPauses = 2 };

        var outcome = WindowPlacer.Place(api, Target, WindowShow.Normal);

        Assert.Equal(PlaceOutcome.WindowGone, outcome);
        Assert.Equal(2, api.Pauses);
    }

    [Fact]
    public void Hung_frame_held_iconic_returns_failed_after_exactly_twenty_pauses_without_a_sync_show()
    {
        var api = new FakePlacementApi { Hung = true, Iconic = true, IgnorePlacementState = true };

        var outcome = WindowPlacer.Place(api, Target, WindowShow.Normal);

        Assert.Equal(PlaceOutcome.Failed, outcome);
        Assert.True(api.Iconic);
        Assert.Equal(20, api.Pauses);
        Assert.DoesNotContain(api.Ops, o => o.StartsWith("Show"));
    }

    [Fact]
    public void Hung_frame_whose_state_applies_after_three_polls_succeeds()
    {
        var api = new FakePlacementApi { Hung = true, Iconic = true, PlacementAppliesAfterPauses = 3 };

        var outcome = WindowPlacer.Place(api, Target, WindowShow.Normal);

        Assert.Equal(PlaceOutcome.Ok, outcome);
        Assert.False(api.Iconic);
        Assert.Equal(3, api.Pauses);
    }

    [Fact]
    public void Non_hung_fallback_that_stays_iconic_after_the_sync_show_goes_async()
    {
        // Hosted frames can ignore SW_SHOWNOACTIVATE and SW_RESTORE. The normal branch must not report
        // success after SetRect while still iconic; it falls into the same bounded async placement path.
        var api = new FakePlacementApi { Hung = false, Iconic = true, IgnoreSynchronousShow = true };

        var outcome = WindowPlacer.Place(api, Target, WindowShow.Normal);

        Assert.Equal(PlaceOutcome.Ok, outcome);
        Assert.False(api.Iconic);
        Assert.Contains("Show4", api.Ops);
        Assert.Contains("Show9", api.Ops);
        Assert.Contains("SetPlacement:Normal", api.Ops);
        Assert.Equal(0, api.Pauses);
    }

    [Fact]
    public void Hung_window_targeting_maximized_sends_the_maximized_command_and_applies_it()
    {
        var api = new FakePlacementApi { Hung = true, Iconic = true };

        var outcome = WindowPlacer.Place(api, Target, WindowShow.Maximized);

        Assert.Equal(PlaceOutcome.Ok, outcome);
        Assert.Contains("SetPlacement:Maximized", api.Ops);
        Assert.False(api.Iconic);
        Assert.True(api.Maximized);
    }

    [Fact]
    public void Hung_window_targeting_minimized_sends_the_minimized_command_and_keeps_it_iconic()
    {
        var api = new FakePlacementApi { Hung = true, Iconic = false };

        var outcome = WindowPlacer.Place(api, Target, WindowShow.Minimized);

        Assert.Equal(PlaceOutcome.Ok, outcome);
        Assert.Contains("SetPlacement:Minimized", api.Ops);
        Assert.True(api.Iconic);
        Assert.False(api.Maximized);
    }

    [Theory]
    [InlineData(WindowShow.Normal, false, false, true, ShowNoActivate)]    // already normal: move without activating
    [InlineData(WindowShow.Normal, true, false, true, ShowRestore)]        // iconic -> normal
    [InlineData(WindowShow.Normal, false, true, true, ShowRestore)]        // maximized -> normal
    [InlineData(WindowShow.Maximized, false, true, true, ShowNA)]          // already max on target: no activation
    [InlineData(WindowShow.Maximized, false, true, false, ShowMaximized)]  // max on another monitor: transition
    [InlineData(WindowShow.Maximized, false, false, true, ShowMaximized)]  // normal -> maximized
    [InlineData(WindowShow.Maximized, true, false, true, ShowMaximized)]   // iconic -> maximized
    [InlineData(WindowShow.Minimized, false, false, true, ShowMinNoActivate)] // minimize without activating
    public void Async_show_command_keeps_nonactivating_state_and_only_transitions_with_restore_or_maximize(
        WindowShow requested, bool minimized, bool maximized, bool onTargetMonitor, int expected)
    {
        Assert.Equal(expected, WindowPlacer.AsyncShowCommand(requested, minimized, maximized, onTargetMonitor));
    }

    [Fact]
    public void Workspace_conversion_subtracts_left_and_top_work_area_offsets_on_a_negative_monitor()
    {
        var monitor = new PixelRect(-2560, 0, 2560, 1440);
        var work = new PixelRect(-2480, 40, 2480, 1367); // left 80 px + top 40 px taskbar
        var screen = new PixelRect(-2400, 100, 800, 600);

        Assert.Equal(new PixelRect(-2480, 60, 800, 600), WindowPlacer.ToWorkspaceRect(screen, monitor, work, toolWindow: false));
    }

    [Fact]
    public void Workspace_conversion_leaves_tool_windows_in_screen_coordinates()
    {
        var monitor = new PixelRect(-2560, 0, 2560, 1440);
        var work = new PixelRect(-2480, 40, 2480, 1367);
        var screen = new PixelRect(-2400, 100, 800, 600);

        Assert.Equal(screen, WindowPlacer.ToWorkspaceRect(screen, monitor, work, toolWindow: true));
    }

    private static void AssertShowBeforeFirstMove(FakePlacementApi api, int command)
    {
        int show = api.Ops.IndexOf("Show" + command);
        int firstMove = api.Ops.FindIndex(o => o.StartsWith("SetRect"));
        Assert.True(show >= 0, "expected Show(" + command + "); ops: " + string.Join(", ", api.Ops));
        Assert.True(firstMove >= 0, "expected a move; ops: " + string.Join(", ", api.Ops));
        Assert.True(show < firstMove, "expected Show(" + command + ") before the first move; ops: " + string.Join(", ", api.Ops));
    }

    /// <summary>
    /// State machine for the SHOW categories FlowDesk uses. By default SHOWNOACTIVATE normalizes iconic and
    /// maximized state like SHOWNORMAL (docs: same size/position, no activation). Setting
    /// <see cref="IgnoreNonActivatingShow"/> injects a hosted window that ignores that request; it is a
    /// hypothetical failure mode, not proof of any real app's behaviour. SetRect never shows the window.
    /// </summary>
    private sealed class FakePlacementApi : IWindowPlacementApi
    {
        public bool WindowExists { get; set; } = true;
        public bool Hung { get; set; }
        public bool PlacementFails { get; set; }
        public bool Iconic { get; set; }
        public bool Maximized { get; set; }
        public bool RestoreWasMaximized { get; set; }
        public bool IgnoreNonActivatingShow { get; set; }
        /// <summary>Every synchronous ShowWindow command is ignored (hosted/frozen frame behaviour).</summary>
        public bool IgnoreSynchronousShow { get; set; }
        /// <summary>The async placement returns true but never applies its show state (held iconic).</summary>
        public bool IgnorePlacementState { get; set; }
        /// <summary>Apply the async placement state only after this many Pause polls (0 = immediately).</summary>
        public int PlacementAppliesAfterPauses { get; set; }
        /// <summary>The window disappears once this many Pause polls have elapsed (null = never).</summary>
        public int? VanishAfterPauses { get; set; }
        public bool OnTargetMonitor { get; set; } = true;
        public bool MoveSucceeds { get; set; } = true;
        public int LastError { get; set; }
        public uint Dpi { get; set; } = 96;
        public PixelRect RestoreRect { get; private set; } = new(10, 10, 800, 600);
        public PixelRect LastRequestedRect { get; private set; }
        public List<string> Ops { get; } = [];
        public int Pauses { get; private set; }
        private WindowShow? _pendingShow;

        public bool IsWindow => WindowExists;
        public bool IsHung => Hung;
        public bool IsMinimized => Iconic;
        public bool IsMaximized => Maximized;
        public bool IsOnTargetMonitor(PixelRect target) => OnTargetMonitor;

        public bool SetRect(PixelRect target, bool asynchronous = false)
        {
            Ops.Add(asynchronous ? "SetRect:async" : "SetRect");
            LastRequestedRect = target;
            if (!WindowExists || !MoveSucceeds) return false;
            // Moving a minimized/hidden window changes its restore rect; it does not show the window.
            RestoreRect = target;
            return true;
        }

        public bool RectMatches(PixelRect target) => RestoreRect == target;

        /// <summary>
        /// Models the async <c>SetWindowPlacement</c> call: it returns the native result and applies the
        /// requested show state immediately unless a delay/ignore switch models the owning thread's lag.
        /// </summary>
        public bool SetPlacementAsync(PixelRect target, WindowShow show)
        {
            Ops.Add("SetPlacement:" + show);
            LastRequestedRect = target;
            if (!WindowExists || PlacementFails) return false;
            RestoreRect = target;
            if (IgnorePlacementState || PlacementAppliesAfterPauses > 0)
            {
                _pendingShow = show;
                return true;
            }
            ApplyShow(show);
            return true;
        }

        private void ApplyShow(WindowShow show)
        {
            switch (show)
            {
                case WindowShow.Normal:
                    Iconic = false;
                    Maximized = false;
                    break;
                case WindowShow.Maximized:
                    Iconic = false;
                    Maximized = true;
                    break;
                case WindowShow.Minimized:
                    Iconic = true;
                    Maximized = false;
                    break;
            }
        }

        public void Show(int command)
        {
            Ops.Add("Show" + command);
            if (IgnoreSynchronousShow) return;
            switch (command)
            {
                case ShowNormal:
                    Iconic = false;
                    Maximized = false;
                    break;
                case ShowNoActivate:
                    // Documented as SHOWNORMAL without activation: restore normal size/position, stay unfocused.
                    if (IgnoreNonActivatingShow) break;
                    Iconic = false;
                    Maximized = false;
                    break;
                case ShowRestore:
                    bool wasIconic = Iconic;
                    Iconic = false;
                    Maximized = wasIconic ? RestoreWasMaximized : false;
                    break;
                case ShowMaximized:
                    if (!Iconic) Maximized = true;
                    else RestoreWasMaximized = true;
                    break;
                case ShowMinNoActivate:
                    if (!Iconic) RestoreWasMaximized = Maximized;
                    Iconic = true;
                    Maximized = false;
                    break;
            }
        }

        public void Pause()
        {
            Pauses++;
            if (VanishAfterPauses is { } vanish && Pauses >= vanish) WindowExists = false;
            if (_pendingShow is { } show && !IgnorePlacementState && Pauses >= PlacementAppliesAfterPauses)
            {
                ApplyShow(show);
                _pendingShow = null;
            }
        }

        public IEnumerable<string> RectMoves() => Ops.Where(o => o.StartsWith("SetRect"));
    }

    /// <summary>
    /// Minimal IWindowSystem whose Place is the real WindowPlacer over a fake API, so a LayoutApplier test can
    /// prove the layout path ends in the same minimized/hung placement logic instead of a separate one.
    /// </summary>
    private sealed class PlacerWindowSystem(FakePlacementApi api, WindowInfo window) : IWindowSystem
    {
        public int OwnProcessId => 1;
        public IReadOnlyList<WindowInfo> GetAppWindows() => [window];
        public WindowInfo? Get(long handle) => handle == window.Handle ? window : null;
        public bool IsAlive(long handle) => handle == window.Handle;
        public IReadOnlyList<MonitorInfo> GetMonitors() =>
            [new("\\\\.\\DISPLAY1", new(0, 0, 1920, 1080), new(0, 0, 1920, 1040), true, 96)];
        public PlaceOutcome Place(long handle, PixelRect normalRect, WindowShow show) =>
            WindowPlacer.Place(api, normalRect, show);
        public (int L, int T, int R, int B) GetInvisibleBorders(long handle) => (0, 0, 0, 0);
        public void BringToFront(long handle) { }
        public bool Activate(long handle) => true;
        public void Close(long handle) { }
        public void Minimize(long handle) { }
    }
}
