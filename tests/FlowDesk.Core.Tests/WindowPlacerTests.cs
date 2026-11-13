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
    public void Hung_window_gets_an_asynchronous_move_and_does_not_block()
    {
        var api = new FakePlacementApi { Hung = true };

        var outcome = WindowPlacer.Place(api, Target, WindowShow.Maximized);

        Assert.Equal(PlaceOutcome.Ok, outcome);
        Assert.Equal(new[] { "SetRect:async" }, api.Ops);
        Assert.Equal(Target, api.LastRequestedRect);
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
        public bool Iconic { get; set; }
        public bool Maximized { get; set; }
        public bool RestoreWasMaximized { get; set; }
        public bool IgnoreNonActivatingShow { get; set; }
        public bool OnTargetMonitor { get; set; } = true;
        public bool MoveSucceeds { get; set; } = true;
        public int LastError { get; set; }
        public uint Dpi { get; set; } = 96;
        public PixelRect RestoreRect { get; private set; } = new(10, 10, 800, 600);
        public PixelRect LastRequestedRect { get; private set; }
        public List<string> Ops { get; } = [];

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

        public void Show(int command)
        {
            Ops.Add("Show" + command);
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

        public void Pause() { }

        public IEnumerable<string> RectMoves() => Ops.Where(o => o.StartsWith("SetRect"));
    }
}
