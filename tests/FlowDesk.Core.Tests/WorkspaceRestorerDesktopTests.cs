using FlowDesk.Core.Desktops;
using FlowDesk.Core.Model;
using FlowDesk.Core.Restore;
using FlowDesk.Core.Windows;

namespace FlowDesk.Core.Tests;

/// <summary>
/// Restore keeps each window's saved virtual desktop: it moves matched and newly launched windows,
/// creates at most one missing adjacent desktop per restore, never switches the user's desktop, and reports an honest
/// PlaceDenied when the move cannot be made or cannot be verified.
/// </summary>
public class WorkspaceRestorerDesktopTests
{
    private const string Code = @"C:\apps\code.exe";
    private const string Notes = @"C:\Windows\notepad.exe";
    private const string CodeTitle = "proj - Visual Studio Code";
    private static readonly Guid DesktopB = Guid.Parse("eeeeeeee-0000-0000-0000-000000000005");
    private static readonly Guid DesktopC = Guid.Parse("ffffffff-0000-0000-0000-000000000006");

    private readonly FakeWindowSystem _sys = new();
    private readonly FakeLauncher _launcher;

    public WorkspaceRestorerDesktopTests() => _launcher = new FakeLauncher(_sys);

    private WorkspaceRestorer Restorer(FakeVirtualDesktops? desktops) =>
        new(_sys, _launcher, new SessionTracker(), _launcher.Delay, () => null, desktops: desktops);

    [Fact]
    public async Task Reused_window_from_another_desktop_is_moved_and_not_activated()
    {
        // IsOnCurrentDesktop=false keeps it out of GetAppWindows; restore must still see it via GetAllAppWindows.
        var w = _sys.Add(Code, CodeTitle, desktopIndex: 0, desktopId: Guid.NewGuid(), isOnCurrentDesktop: false);
        var desktops = new FakeVirtualDesktops { Count = 3, Current = 0 };
        desktops.DesktopNumbers[w.Handle] = 0;
        var item = Build.App(Code, CodeTitle);
        item.Window!.DesktopIndex = 2;

        var report = await Restorer(desktops).RestoreAsync(Workspace(item), Build.Fast());

        Assert.Equal(ItemOutcome.Reused, report.Items[0].Outcome);
        Assert.Equal([2], desktops.Moves);
        Assert.Empty(desktops.Gotos);
        // OnCurrent is deliberately left at the fake's default (true): after the move the
        // authoritative desktop number (2) differs from the current one (0), so no foregrounding.
        Assert.Null(_sys.Activated);
        Assert.Empty(_sys.Fronted);
    }

    [Fact]
    public async Task Restored_off_desktop_window_is_not_foregrounded_when_the_numbers_disagree()
    {
        // Stale/optimistic metadata and the fake's back-compat default both say "current"; the
        // actual desktop number (2) after the move is authoritative and must stop foregrounding.
        var w = _sys.Add(Code, CodeTitle, desktopIndex: 0, isOnCurrentDesktop: true);
        var desktops = new FakeVirtualDesktops { Count = 3, Current = 0 };
        desktops.DesktopNumbers[w.Handle] = 0;
        var item = Build.App(Code, CodeTitle);
        item.Window!.DesktopIndex = 2;

        var report = await Restorer(desktops).RestoreAsync(Workspace(item), Build.Fast());

        Assert.Equal(ItemOutcome.Reused, report.Items[0].Outcome);
        Assert.Equal([2], desktops.Moves);
        Assert.Null(_sys.Activated);
        Assert.Empty(_sys.Fronted);
    }

    [Theory]
    [InlineData(WindowShow.Minimized, WindowShow.Normal)]
    [InlineData(WindowShow.Maximized, WindowShow.Normal)]
    public async Task Background_show_change_stages_through_the_current_desktop_and_returns(WindowShow initial, WindowShow savedShow)
    {
        // Number 1 vs current 0 is authoritative: metadata says background, the fake's OnCurrent default says current.
        var w = _sys.Add(Code, CodeTitle, show: initial, desktopIndex: 1, isOnCurrentDesktop: false);
        var desktops = new FakeVirtualDesktops { Count = 3, Current = 0 };
        desktops.DesktopNumbers[w.Handle] = 1;
        var item = Build.App(Code, CodeTitle, show: savedShow);
        item.Window!.DesktopIndex = 2;

        var report = await Restorer(desktops).RestoreAsync(Workspace(item), Build.Fast());

        Assert.Equal(ItemOutcome.Reused, report.Items[0].Outcome);
        Assert.Equal([0, 2], desktops.Moves); // stage on current, then saved desktop
        Assert.Empty(desktops.Gotos);
        Assert.Equal(savedShow, _sys.Get(w.Handle)!.Show); // state applied while staged
        Assert.Null(_sys.Activated);
        Assert.Empty(_sys.Fronted);
    }

    [Fact]
    public async Task Normal_to_minimized_background_skips_staging_but_moves_to_the_saved_desktop()
    {
        var w = _sys.Add(Code, CodeTitle, show: WindowShow.Normal, desktopIndex: 1, isOnCurrentDesktop: false);
        var desktops = new FakeVirtualDesktops { Count = 3, Current = 0 };
        desktops.DesktopNumbers[w.Handle] = 1;
        var item = Build.App(Code, CodeTitle, show: WindowShow.Minimized);
        item.Window!.DesktopIndex = 2;

        var report = await Restorer(desktops).RestoreAsync(Workspace(item), Build.Fast());

        Assert.Equal(ItemOutcome.Reused, report.Items[0].Outcome);
        Assert.Equal([2], desktops.Moves); // non-activating minimize needs no staging
        Assert.Empty(desktops.Gotos);
        Assert.Equal(WindowShow.Minimized, _sys.Get(w.Handle)!.Show);
        Assert.Null(_sys.Activated);
    }

    [Fact]
    public async Task Already_maximized_missing_backend_with_known_target_index_succeeds_without_staging()
    {
        var w = _sys.Add(Code, CodeTitle, show: WindowShow.Maximized, desktopIndex: 2, isOnCurrentDesktop: false);
        var item = Build.App(Code, CodeTitle, show: WindowShow.Maximized);
        item.Window!.DesktopIndex = 2;

        var report = await Restorer(null).RestoreAsync(Workspace(item), Build.Fast());

        Assert.Equal(ItemOutcome.Reused, report.Items[0].Outcome);
        Assert.Equal(1, report.Placed);
        Assert.Contains(_sys.Placed, p => p.Handle == w.Handle); // placed without any staging move
        Assert.Null(_sys.Activated);
        Assert.Empty(_sys.Fronted);
    }

    [Fact]
    public async Task Already_minimized_background_skips_staging_and_only_moves_to_saved()
    {
        var w = _sys.Add(Code, CodeTitle, show: WindowShow.Minimized, desktopIndex: 1, isOnCurrentDesktop: false);
        var desktops = new FakeVirtualDesktops { Count = 3, Current = 0 };
        desktops.DesktopNumbers[w.Handle] = 1;
        var item = Build.App(Code, CodeTitle, show: WindowShow.Minimized);
        item.Window!.DesktopIndex = 2;

        var report = await Restorer(desktops).RestoreAsync(Workspace(item), Build.Fast());

        Assert.Equal(ItemOutcome.Reused, report.Items[0].Outcome);
        Assert.Equal([2], desktops.Moves);
        Assert.Empty(desktops.Gotos);
        Assert.Null(_sys.Activated);
    }

    [Fact]
    public async Task Same_guid_target_uses_the_original_index_not_the_stored_number()
    {
        var w = _sys.Add(Code, CodeTitle, show: WindowShow.Minimized, desktopIndex: 1, desktopId: DesktopB,
            isOnCurrentDesktop: false);
        var desktops = new FakeVirtualDesktops { Count = 3, Current = 0 };
        desktops.DesktopNumbers[w.Handle] = 1;
        desktops.DesktopIds[w.Handle] = DesktopB;
        var currentId = Guid.Parse("99999999-0000-0000-0000-000000000009");
        // The window's desktop identity changes with each move, as the real backend's would.
        desktops.OnMoveWindow = () =>
        {
            if (desktops.Moves[^1] == 0) desktops.DesktopIds[w.Handle] = currentId;
            else if (desktops.Moves[^1] == 1) desktops.DesktopIds[w.Handle] = DesktopB;
        };
        var item = Build.App(Code, CodeTitle, show: WindowShow.Normal);
        item.Window!.DesktopIndex = 3; // stored number differs from the original desktop (1)
        item.Window.DesktopId = DesktopB;

        var report = await Restorer(desktops).RestoreAsync(Workspace(item), Build.Fast());

        Assert.Equal(ItemOutcome.Reused, report.Items[0].Outcome);
        Assert.Equal([0, 1], desktops.Moves); // back to the matching GUID's original desktop
        Assert.Equal(0, desktops.CreateCalls); // no desktop 3 created for a same-GUID target
        Assert.Empty(desktops.Gotos);
        Assert.Null(_sys.Activated);
    }

    [Fact]
    public async Task Missing_backend_cannot_stage_a_background_state_change()
    {
        var w = _sys.Add(Code, CodeTitle, show: WindowShow.Minimized, desktopIndex: 1, isOnCurrentDesktop: false);
        var item = Build.App(Code, CodeTitle, show: WindowShow.Normal);
        item.Window!.DesktopIndex = 2;

        var report = await Restorer(null).RestoreAsync(Workspace(item), Build.Fast());

        Assert.Equal(ItemOutcome.PlaceDenied, report.Items[0].Outcome);
        Assert.Equal(0, report.Placed);
        Assert.Empty(_sys.Placed); // never attempt the show-state transition without staging
        Assert.Null(_sys.Activated);
    }

    [Fact]
    public async Task Failed_final_desktop_move_rolls_back_the_original_desktop()
    {
        var w = _sys.Add(Code, CodeTitle, show: WindowShow.Minimized, desktopIndex: 1, isOnCurrentDesktop: false);
        var desktops = new FakeVirtualDesktops { Count = 3, Current = 0 };
        desktops.DesktopNumbers[w.Handle] = 1;
        desktops.FailMoveIndices.Add(2); // only the final move fails
        var item = Build.App(Code, CodeTitle, show: WindowShow.Normal);
        item.Window!.DesktopIndex = 2;

        var report = await Restorer(desktops).RestoreAsync(Workspace(item), Build.Fast());

        Assert.Equal(ItemOutcome.PlaceDenied, report.Items[0].Outcome);
        Assert.Equal([0, 2, 1], desktops.Moves); // stage, failed final move, rollback
        Assert.Equal(1, desktops.DesktopNumbers[w.Handle]);
        Assert.Contains(_sys.Placed, p => p.Handle == w.Handle);
        Assert.Null(_sys.Activated);
        Assert.Empty(_sys.Fronted);
    }

    [Fact]
    public async Task Cancellation_after_staging_rolls_back_the_original_desktop()
    {
        var w = _sys.Add(Code, CodeTitle, show: WindowShow.Minimized, desktopIndex: 1, isOnCurrentDesktop: false);
        var desktops = new FakeVirtualDesktops { Count = 3, Current = 0 };
        desktops.DesktopNumbers[w.Handle] = 1;
        using var cts = new CancellationTokenSource();
        desktops.OnMoveWindow = () => { if (desktops.Moves[^1] == 0) cts.Cancel(); };
        var item = Build.App(Code, CodeTitle, show: WindowShow.Normal);
        item.Window!.DesktopIndex = 2;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Restorer(desktops).RestoreAsync(Workspace(item), Build.Fast(), ct: cts.Token));

        Assert.Equal([0, 1], desktops.Moves);
        Assert.Equal(1, desktops.DesktopNumbers[w.Handle]);
        Assert.Empty(desktops.Gotos);
    }

    [Fact]
    public async Task Current_desktop_changing_during_staging_fails_and_rolls_back()
    {
        var w = _sys.Add(Code, CodeTitle, show: WindowShow.Minimized, desktopIndex: 1, isOnCurrentDesktop: false);
        var desktops = new FakeVirtualDesktops { Count = 3, Current = 0 };
        desktops.DesktopNumbers[w.Handle] = 1;
        desktops.OnMoveWindow = () => { if (desktops.Moves[^1] == 0) desktops.Current = 1; };
        var item = Build.App(Code, CodeTitle, show: WindowShow.Normal);
        item.Window!.DesktopIndex = 2;

        var report = await Restorer(desktops).RestoreAsync(Workspace(item), Build.Fast());

        Assert.Equal(ItemOutcome.PlaceDenied, report.Items[0].Outcome);
        Assert.Equal([0, 1], desktops.Moves); // staged, then rolled back
        Assert.Equal(1, desktops.DesktopNumbers[w.Handle]);
        Assert.Empty(_sys.Placed); // failed before the show-state placement
        Assert.Empty(desktops.Gotos);
    }

    [Fact]
    public async Task Rollback_failure_is_appended_to_the_place_denied_detail()
    {
        var w = _sys.Add(Code, CodeTitle, show: WindowShow.Minimized, desktopIndex: 1, isOnCurrentDesktop: false);
        var desktops = new FakeVirtualDesktops { Count = 3, Current = 0 };
        desktops.DesktopNumbers[w.Handle] = 1;
        desktops.FailMoveIndices.Add(2); // saved-desktop move fails
        desktops.FailMoveIndices.Add(1); // rollback to the original desktop also fails
        var item = Build.App(Code, CodeTitle, show: WindowShow.Normal);
        item.Window!.DesktopIndex = 2;

        var report = await Restorer(desktops).RestoreAsync(Workspace(item), Build.Fast());

        Assert.Equal([0, 2, 1], desktops.Moves);
        Assert.Equal(ItemOutcome.PlaceDenied, report.Items[0].Outcome);
        Assert.Equal(0, report.Placed);
        string detail = report.Items[0].Detail!;
        Assert.Contains("original desktop", detail);
        Assert.Contains("failed or could not be verified", detail);
    }

    [Fact]
    public async Task Cancelled_restore_with_failed_rollback_throws_with_the_cancellation_cause()
    {
        var w = _sys.Add(Code, CodeTitle, show: WindowShow.Minimized, desktopIndex: 1, isOnCurrentDesktop: false);
        var desktops = new FakeVirtualDesktops { Count = 3, Current = 0 };
        desktops.DesktopNumbers[w.Handle] = 1;
        desktops.FailMoveIndices.Add(1); // only the rollback fails
        using var cts = new CancellationTokenSource();
        desktops.OnMoveWindow = () => { if (desktops.Moves[^1] == 0) cts.Cancel(); };
        var item = Build.App(Code, CodeTitle, show: WindowShow.Normal);
        item.Window!.DesktopIndex = 2;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Restorer(desktops).RestoreAsync(Workspace(item), Build.Fast(), ct: cts.Token));

        Assert.IsAssignableFrom<OperationCanceledException>(ex.InnerException);
        Assert.Contains("original desktop", ex.Message);
        Assert.Equal([0, 1], desktops.Moves);
    }

    [Fact]
    public async Task Stale_guid_falls_back_to_the_saved_index_when_the_identity_no_longer_matches()
    {
        var originalId = Guid.Parse("aaaaaaaa-1111-0000-0000-000000000001");
        var staleSavedId = Guid.Parse("bbbbbbbb-2222-0000-0000-000000000002");
        var liveId = Guid.Parse("cccccccc-3333-0000-0000-000000000003");
        var w = _sys.Add(Code, CodeTitle, show: WindowShow.Minimized, desktopIndex: 1, desktopId: originalId,
            isOnCurrentDesktop: false);
        var desktops = new FakeVirtualDesktops { Count = 3, Current = 0 };
        desktops.DesktopNumbers[w.Handle] = 1;
        desktops.DesktopIds[w.Handle] = originalId;
        desktops.OnMoveWindow = () => { if (desktops.Moves[^1] == 0) desktops.DesktopIds[w.Handle] = liveId; };
        var item = Build.App(Code, CodeTitle, show: WindowShow.Normal);
        item.Window!.DesktopIndex = 0; // stale GUID + saved number 0 (= current)
        item.Window.DesktopId = staleSavedId;

        var report = await Restorer(desktops).RestoreAsync(Workspace(item), Build.Fast());

        Assert.Equal(ItemOutcome.Reused, report.Items[0].Outcome);
        Assert.Equal([0], desktops.Moves); // intended index fallback: stays on current 0, not back to 1
        Assert.Empty(desktops.Gotos);
    }

    [Fact]
    public async Task Empty_guid_old_data_returns_the_staged_window_to_its_original_desktop()
    {
        // Old JSON with no usable saved desktop: staging must still restore the original index,
        // not strand the window on the current desktop.
        var w = _sys.Add(Code, CodeTitle, show: WindowShow.Minimized, desktopIndex: 1, isOnCurrentDesktop: false);
        var desktops = new FakeVirtualDesktops { Count = 3, Current = 0 };
        desktops.DesktopNumbers[w.Handle] = 1;
        var item = Build.App(Code, CodeTitle, show: WindowShow.Normal);
        item.Window!.DesktopIndex = null;
        item.Window.DesktopId = Guid.Empty;

        var report = await Restorer(desktops).RestoreAsync(Workspace(item), Build.Fast());

        Assert.Equal(ItemOutcome.Reused, report.Items[0].Outcome);
        Assert.Equal([0, 1], desktops.Moves);
        Assert.Equal(1, desktops.DesktopNumbers[w.Handle]);
        Assert.Empty(desktops.Gotos);
    }

    [Fact]
    public async Task Unknown_desktop_plus_cloak_is_treated_as_nonlocal_and_refuses_staging()
    {
        var w = _sys.Add(Code, CodeTitle, show: WindowShow.Minimized, isOnCurrentDesktop: null, cloakReason: 2);
        var desktops = new FakeVirtualDesktops { ReadsReturnNull = true };
        desktops.OnCurrent[w.Handle] = null; // all-null backend: no proof the window is local
        var item = Build.App(Code, CodeTitle, show: WindowShow.Normal);

        var report = await Restorer(desktops).RestoreAsync(Workspace(item), Build.Fast());

        Assert.Equal(ItemOutcome.PlaceDenied, report.Items[0].Outcome);
        Assert.Equal(0, report.Placed);
        Assert.Empty(_sys.Placed); // never attempt an activating show-state transition
        Assert.Empty(desktops.Moves);
        Assert.Empty(desktops.Gotos);
    }

    [Fact]
    public async Task Corrupt_huge_desktop_index_never_creates_desktops()
    {
        var w = _sys.Add(Code, CodeTitle, desktopIndex: 0, isOnCurrentDesktop: true);
        var desktops = new FakeVirtualDesktops { Count = 1, Current = 0 };
        desktops.DesktopNumbers[w.Handle] = 0;
        var item = Build.App(Code, CodeTitle);
        item.Window!.DesktopIndex = int.MaxValue;

        var report = await Restorer(desktops).RestoreAsync(Workspace(item), Build.Fast());

        Assert.Equal(ItemOutcome.PlaceDenied, report.Items[0].Outcome);
        Assert.Equal(0, desktops.CreateCalls);
        Assert.Empty(desktops.Moves);
    }

    [Fact]
    public async Task Large_existing_desktop_index_is_allowed_when_the_count_is_already_high()
    {
        var w = _sys.Add(Code, CodeTitle, desktopIndex: 0, isOnCurrentDesktop: true);
        var desktops = new FakeVirtualDesktops { Count = 200, Current = 0 };
        desktops.DesktopNumbers[w.Handle] = 0;
        var item = Build.App(Code, CodeTitle);
        item.Window!.DesktopIndex = 100;

        var report = await Restorer(desktops).RestoreAsync(Workspace(item), Build.Fast());

        Assert.Equal(ItemOutcome.Reused, report.Items[0].Outcome);
        Assert.Equal(0, desktops.CreateCalls);
        Assert.Equal([100], desktops.Moves);
    }

    [Fact]
    public async Task Late_launched_window_is_moved_to_its_saved_desktop()
    {
        var item = Build.App(Notes, "notes.txt - Notepad");
        item.Window!.DesktopIndex = 1;
        _launcher.Behaviour[item.Id] = (Notes, "notes.txt - Notepad", 1);
        var desktops = new FakeVirtualDesktops { Count = 2, Current = 0 };

        var report = await Restorer(desktops).RestoreAsync(Workspace(item), Build.Fast());

        Assert.Equal(ItemOutcome.Launched, report.Items[0].Outcome);
        Assert.Equal([1], desktops.Moves);
        Assert.Empty(desktops.Gotos);
    }

    [Fact]
    public async Task One_missing_adjacent_desktop_is_created()
    {
        var w = _sys.Add(Code, CodeTitle, desktopIndex: 0, isOnCurrentDesktop: true);
        var desktops = new FakeVirtualDesktops { Count = 1, Current = 0 };
        desktops.DesktopNumbers[w.Handle] = 0;
        var item = Build.App(Code, CodeTitle);
        item.Window!.DesktopIndex = 1;

        var report = await Restorer(desktops).RestoreAsync(Workspace(item), Build.Fast());

        Assert.Equal(ItemOutcome.Reused, report.Items[0].Outcome);
        Assert.Equal(1, desktops.CreateCalls);
        Assert.Equal([1], desktops.Moves);
        Assert.Empty(desktops.Gotos);
    }

    [Theory]
    [InlineData(3)] // count + 1 needs two new desktops
    [InlineData(5)]
    public async Task Missing_desktop_beyond_adjacent_creates_none_and_reports_manual_recovery(int target)
    {
        var w = _sys.Add(Code, CodeTitle, desktopIndex: 0, isOnCurrentDesktop: true);
        var desktops = new FakeVirtualDesktops { Count = 2 };
        desktops.DesktopNumbers[w.Handle] = 0;
        var item = Build.App(Code, CodeTitle);
        item.Window!.DesktopIndex = target;
        var report = await Restorer(desktops).RestoreAsync(Workspace(item), Build.Fast());
        Assert.Equal(ItemOutcome.PlaceDenied, report.Items[0].Outcome);
        Assert.Contains($"Desktop {target + 1} does not exist (you have 2)", report.Items[0].Detail);
        Assert.Contains("Add desktops in Task View", report.Items[0].Detail);
        Assert.Equal(0, desktops.CreateCalls);
        Assert.Empty(desktops.Moves);
        Assert.Empty(desktops.Gotos);
        Assert.Equal(0, desktops.DesktopNumbers[w.Handle]);
    }

    [Theory]
    [InlineData(1, false)] // both items share the single created desktop
    [InlineData(2, false)] // cannot grow a chain of adjacent desktops, one per item
    [InlineData(1, true)]  // a failed creation also consumes the attempt budget
    public async Task Desktop_creation_budget_is_shared_by_all_items(int secondTarget, bool createFails)
    {
        var a = _sys.Add(Code, CodeTitle, desktopIndex: 0, isOnCurrentDesktop: true);
        var b = _sys.Add(Notes, "Notes", desktopIndex: 0, isOnCurrentDesktop: true);
        var desktops = new FakeVirtualDesktops { Count = 1, CreateFails = createFails };
        desktops.DesktopNumbers[a.Handle] = desktops.DesktopNumbers[b.Handle] = 0;
        var first = Build.App(Code, CodeTitle);
        var second = Build.App(Notes, "Notes");
        first.Window!.DesktopIndex = 1;
        second.Window!.DesktopIndex = secondTarget;
        var report = await Restorer(desktops).RestoreAsync(Workspace(first, second), Build.Fast());
        Assert.Equal(1, desktops.CreateCalls);
        Assert.Equal(createFails ? ItemOutcome.PlaceDenied : ItemOutcome.Reused, report.Items[0].Outcome);
        Assert.Equal(!createFails && secondTarget == 1 ? ItemOutcome.Reused : ItemOutcome.PlaceDenied, report.Items[1].Outcome);
        Assert.Equal(createFails ? Array.Empty<int>() : secondTarget == 1 ? new[] { 1, 1 } : new[] { 1 }, desktops.Moves);
        Assert.Empty(desktops.Gotos);
    }

    [Fact]
    public async Task No_desktop_is_created_when_it_already_exists()
    {
        var w = _sys.Add(Code, CodeTitle, desktopIndex: 0, isOnCurrentDesktop: true);
        var desktops = new FakeVirtualDesktops { Count = 3, Current = 0 };
        desktops.DesktopNumbers[w.Handle] = 0;
        var item = Build.App(Code, CodeTitle);
        item.Window!.DesktopIndex = 2;

        var report = await Restorer(desktops).RestoreAsync(Workspace(item), Build.Fast());

        Assert.Equal(ItemOutcome.Reused, report.Items[0].Outcome);
        Assert.Equal(0, desktops.CreateCalls);
        Assert.Equal([2], desktops.Moves);
        Assert.Empty(desktops.Gotos);
    }

    [Fact]
    public async Task A_known_guid_that_already_matches_needs_no_move()
    {
        var w = _sys.Add(Code, CodeTitle, desktopIndex: 0, desktopId: DesktopB, isOnCurrentDesktop: false);
        // Fewer desktops than the saved index: a GUID match must still avoid create/move.
        var desktops = new FakeVirtualDesktops { Count = 1, Current = 0 };
        desktops.DesktopNumbers[w.Handle] = 0;
        desktops.DesktopIds[w.Handle] = DesktopB;
        var item = Build.App(Code, CodeTitle);
        item.Window!.DesktopIndex = 2;
        item.Window.DesktopId = DesktopB;

        var report = await Restorer(desktops).RestoreAsync(Workspace(item), Build.Fast());

        Assert.Equal(ItemOutcome.Reused, report.Items[0].Outcome);
        Assert.Empty(desktops.Moves);
        Assert.Equal(0, desktops.CreateCalls);
    }

    [Fact]
    public async Task Guid_only_with_a_different_desktop_is_reported_unavailable()
    {
        var w = _sys.Add(Code, CodeTitle, desktopIndex: 0, desktopId: DesktopC, isOnCurrentDesktop: false);
        var desktops = new FakeVirtualDesktops { Count = 3, Current = 0 };
        desktops.DesktopIds[w.Handle] = DesktopC;
        var item = Build.App(Code, CodeTitle);
        item.Window!.DesktopIndex = null;
        item.Window.DesktopId = DesktopB;

        var report = await Restorer(desktops).RestoreAsync(Workspace(item), Build.Fast());

        Assert.Equal(ItemOutcome.PlaceDenied, report.Items[0].Outcome);
        Assert.Equal(0, report.Placed);
        Assert.Empty(desktops.Moves);
        Assert.Equal(0, desktops.CreateCalls);
    }

    [Fact]
    public async Task Missing_dll_denies_an_off_desktop_window_but_accepts_one_already_on_target()
    {
        var target = _sys.Add(Code, CodeTitle, desktopIndex: 2, isOnCurrentDesktop: false);
        var item = Build.App(Code, CodeTitle);
        item.Window!.DesktopIndex = 2;

        var report = await Restorer(null).RestoreAsync(Workspace(item), Build.Fast());

        Assert.Equal(ItemOutcome.Reused, report.Items[0].Outcome); // proven from the actual window metadata

        _sys.CloseWindow(target.Handle);
        var unknown = _sys.Add(Code, CodeTitle, isOnCurrentDesktop: false);
        var item2 = Build.App(Code, CodeTitle);
        item2.Window!.DesktopIndex = 2;
        var report2 = await Restorer(null).RestoreAsync(Workspace(item2), Build.Fast());

        Assert.Equal(ItemOutcome.PlaceDenied, report2.Items[0].Outcome);
        Assert.Equal(0, report2.Placed);
    }

    [Fact]
    public async Task Failed_move_is_reported_denied_and_not_counted_as_placed()
    {
        var w = _sys.Add(Code, CodeTitle, desktopIndex: 0, isOnCurrentDesktop: true);
        var desktops = new FakeVirtualDesktops { Count = 3, Current = 0, MoveFails = true };
        desktops.DesktopNumbers[w.Handle] = 0;
        var item = Build.App(Code, CodeTitle);
        item.Window!.DesktopIndex = 2;

        var report = await Restorer(desktops).RestoreAsync(Workspace(item), Build.Fast());

        Assert.Equal(ItemOutcome.PlaceDenied, report.Items[0].Outcome);
        Assert.Equal([2], desktops.Moves);
        Assert.Equal(0, report.Placed);
        Assert.False(report.AllPlaced);
    }

    [Fact]
    public async Task A_move_that_cannot_be_verified_is_reported_denied()
    {
        var w = _sys.Add(Code, CodeTitle, desktopIndex: 0, isOnCurrentDesktop: true);
        var desktops = new FakeVirtualDesktops { Count = 3, Current = 0, ApplyMoves = false };
        desktops.DesktopNumbers[w.Handle] = 0;
        var item = Build.App(Code, CodeTitle);
        item.Window!.DesktopIndex = 2;

        var report = await Restorer(desktops).RestoreAsync(Workspace(item), Build.Fast());

        Assert.Equal(ItemOutcome.PlaceDenied, report.Items[0].Outcome);
        Assert.Equal([2], desktops.Moves);
        Assert.Equal(0, report.Placed);
    }

    [Fact]
    public async Task Old_saved_window_without_a_desktop_does_no_desktop_work()
    {
        var w = _sys.Add(Code, CodeTitle, desktopIndex: 0, isOnCurrentDesktop: true);
        var desktops = new FakeVirtualDesktops { Count = 2, Current = 0 };
        desktops.DesktopNumbers[w.Handle] = 0;
        var item = Build.App(Code, CodeTitle); // Window has null DesktopIndex/DesktopId

        var report = await Restorer(desktops).RestoreAsync(Workspace(item), Build.Fast());

        Assert.Equal(ItemOutcome.Reused, report.Items[0].Outcome);
        Assert.Empty(desktops.Moves);
        Assert.Equal(0, desktops.CreateCalls);
        Assert.Empty(desktops.Gotos);
    }

    [Fact]
    public async Task Desktop_failure_is_reported_once_and_not_counted_as_placed()
    {
        var w = _sys.Add(Code, CodeTitle, desktopIndex: 0, isOnCurrentDesktop: true);
        var desktops = new FakeVirtualDesktops { Count = 3, Current = 0, MoveFails = true };
        desktops.DesktopNumbers[w.Handle] = 0;
        var item = Build.App(Code, CodeTitle);
        item.Window!.DesktopIndex = 2;
        // Synchronous collector: Progress<T> posts asynchronously and the assertions would race.
        var progress = new ProgressLog();

        var report = await Restorer(desktops).RestoreAsync(Workspace(item), Build.Fast(), progress);

        var terminal = progress.Items.Where(p => p.Outcome != ItemOutcome.Pending).ToList();
        Assert.Single(terminal);
        Assert.Equal(ItemOutcome.PlaceDenied, terminal[0].Outcome);
        Assert.Equal(ItemOutcome.PlaceDenied, report.Items[0].Outcome);
        Assert.Equal(0, report.Placed);
    }

    private static Workspace Workspace(params WorkspaceItem[] items) => new() { Items = [.. items] };

    private sealed class ProgressLog : IProgress<ItemResult>
    {
        public List<ItemResult> Items { get; } = [];
        public void Report(ItemResult value) => Items.Add(value);
    }
}
