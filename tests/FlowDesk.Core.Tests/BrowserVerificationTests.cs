using FlowDesk.Core.Model;
using FlowDesk.Core.Restore;
using FlowDesk.Core.Windows;

namespace FlowDesk.Core.Tests;

/// <summary>
/// TASK-006c: final, bounded verification of LAUNCHED browser windows after the settle passes.
/// A persistent size/position mismatch must become PlaceDenied (with requested/actual dimensions),
/// a closed/recycled window must become LaunchedNoWindow, and nothing may keep enforcing.
/// Reused browser windows are outside this scope. Deferred browser finals are published once, after
/// verification, including sibling timeout/launch-failure/skip paths.
/// </summary>
public class BrowserVerificationTests
{
    private const string Chrome = @"C:\Program Files\Google\Chrome\Application\chrome.exe";
    private const string Profile = @"--user-data-dir=D:\Temp\chrome-profile";
    private static readonly PixelRect Target = new(80, 600, 640, 420);
    private static readonly PixelRect ActualMin = new(80, 600, 793, 822);
    private const string DetailPrefix = "The browser controls its window size or position";

    [Fact]
    public async Task Persistent_chrome_size_mismatch_is_PlaceDenied_with_dimensions_and_not_Placed()
    {
        var a = Arrange(request: ClampToMinimum);
        var progress = new ProgressLog();

        var report = await Restorer(a.Sys, a.Launcher).RestoreAsync(Workspace(a.Item), Settling(), progress);

        var result = Assert.Single(report.Items);
        Assert.Equal(ItemOutcome.PlaceDenied, result.Outcome);
        Assert.False(report.AllPlaced);
        Assert.Equal(0, report.Placed);
        Assert.NotNull(result.Detail);
        Assert.StartsWith(DetailPrefix, result.Detail!);
        // Requested (640x420) and actual (793x822) dimensions, not just a "minimum" claim.
        Assert.Contains("640", result.Detail!);
        Assert.Contains("420", result.Detail!);
        Assert.Contains("793", result.Detail!);
        Assert.Contains("822", result.Detail!);
        // The final outcome must be published exactly once, and only after verification.
        AssertFinalOnce(progress, a.Item.Id, ItemOutcome.PlaceDenied);
    }

    [Fact]
    public async Task Launched_chrome_window_closed_before_verification_is_LaunchedNoWindow()
    {
        var inner = new FakeWindowSystem();
        var launcher = new FakeLauncher(inner);
        var item = BrowserItem();
        launcher.Opens[item.Id] = [(Chrome, "Inbox - Google Chrome", 1, Profile)];
        var sys = new ProbeWindows(inner);
        var delay = AfterFirstPlacement(launcher, sys, () =>
        {
            foreach (var w in inner.Windows.ToList()) inner.CloseWindow(w.Handle);
        });

        var report = await new WorkspaceRestorer(sys, launcher, new SessionTracker(), delay)
            .RestoreAsync(Workspace(item), Settling());

        Assert.Equal(ItemOutcome.LaunchedNoWindow, report.Items.Single().Outcome);
        Assert.Equal(0, report.Placed);
    }

    [Fact]
    public async Task Launched_chrome_handle_recycled_before_verification_is_LaunchedNoWindow_without_moving()
    {
        var inner = new FakeWindowSystem();
        var launcher = new FakeLauncher(inner);
        var item = BrowserItem();
        launcher.Opens[item.Id] = [(Chrome, "Inbox - Google Chrome", 1, Profile)];
        var sys = new ProbeWindows(inner);
        var delay = AfterFirstPlacement(launcher, sys, () =>
        {
            var w = Assert.Single(inner.Windows);
            // Windows recycled the handle for a different process: identity is no longer ours.
            inner.Windows[0] = w with { ProcessId = w.ProcessId + 999, NormalRect = new PixelRect(5, 5, 300, 200) };
        });

        var report = await new WorkspaceRestorer(sys, launcher, new SessionTracker(), delay)
            .RestoreAsync(Workspace(item), Settling());

        Assert.Equal(ItemOutcome.LaunchedNoWindow, report.Items.Single().Outcome);
        Assert.Equal(1, sys.PlaceCount); // the original placement only; recycled identity is never moved
        Assert.Equal(new PixelRect(5, 5, 300, 200), Assert.Single(inner.Windows).NormalRect);
    }

    [Fact]
    public async Task Settle_delay_zero_still_verifies_persistent_chrome_mismatch()
    {
        var a = Arrange(request: ClampToMinimum);
        var options = new RestoreOptions
        {
            LaunchTimeout = TimeSpan.FromSeconds(20),
            PollInterval = TimeSpan.FromSeconds(1),
            SettleDelay = TimeSpan.Zero,
        };

        var report = await Restorer(a.Sys, a.Launcher).RestoreAsync(Workspace(a.Item), options);

        var result = Assert.Single(report.Items);
        Assert.Equal(ItemOutcome.PlaceDenied, result.Outcome);
        Assert.Equal(0, report.Placed);
        Assert.StartsWith(DetailPrefix, result.Detail!);
        Assert.Contains("640", result.Detail!);
        Assert.Contains("793", result.Detail!);
    }

    [Fact]
    public async Task Exact_chrome_placement_stays_Launched_and_places_once()
    {
        var a = Arrange();

        var report = await Restorer(a.Sys, a.Launcher).RestoreAsync(Workspace(a.Item), Settling());

        Assert.Equal(ItemOutcome.Launched, report.Items.Single().Outcome);
        Assert.Equal(1, report.Placed);
        Assert.Equal(1, a.Sys.PlaceCount);
        Assert.Equal(Target, Assert.Single(a.Inner.Windows).NormalRect);
    }

    [Fact]
    public async Task Late_chrome_resize_is_repaired_and_stays_Launched()
    {
        var inner = new FakeWindowSystem();
        var launcher = new FakeLauncher(inner);
        var item = BrowserItem();
        launcher.Opens[item.Id] = [(Chrome, "Inbox - Google Chrome", 1, Profile)];
        var sys = new ProbeWindows(inner);
        var delay = AfterFirstPlacement(launcher, sys, () =>
        {
            var w = Assert.Single(inner.Windows);
            inner.Windows[0] = w with { NormalRect = Target with { Height = 822 } };
        });

        var report = await new WorkspaceRestorer(sys, launcher, new SessionTracker(), delay)
            .RestoreAsync(Workspace(item), Settling());

        Assert.Equal(ItemOutcome.Launched, report.Items.Single().Outcome);
        Assert.Equal(Target, Assert.Single(inner.Windows).NormalRect);
        Assert.Equal(2, sys.PlaceCount); // initial placement + one bounded repair
    }

    [Fact]
    public async Task Only_two_bounded_settle_corrections_happen()
    {
        var inner = new FakeWindowSystem();
        var launcher = new FakeLauncher(inner);
        var item = BrowserItem();
        launcher.Opens[item.Id] = [(Chrome, "Inbox - Google Chrome", 1, Profile)];
        var sys = new ProbeWindows(inner);
        int resizes = 0;
        var delay = (TimeSpan _, CancellationToken ct) =>
        {
            var d = launcher.Delay(TimeSpan.Zero, ct);
            if (sys.PlaceCount > 0 && resizes < 2)
            {
                var w = Assert.Single(inner.Windows);
                inner.Windows[0] = w with { NormalRect = Target with { Height = 822 + resizes * 10 } };
                resizes++;
            }
            return d;
        };

        var report = await new WorkspaceRestorer(sys, launcher, new SessionTracker(), delay)
            .RestoreAsync(Workspace(item), Settling());

        Assert.Equal(ItemOutcome.Launched, report.Items.Single().Outcome);
        Assert.Equal(3, sys.PlaceCount); // initial + exactly two settle passes, no endless enforcement
        Assert.Equal(Target, Assert.Single(inner.Windows).NormalRect);
    }

    [Fact]
    public async Task Browser_final_progress_is_published_once_while_corrections_happen()
    {
        var inner = new FakeWindowSystem();
        var launcher = new FakeLauncher(inner);
        var item = BrowserItem();
        launcher.Opens[item.Id] = [(Chrome, "Inbox - Google Chrome", 1, Profile)];
        var sys = new ProbeWindows(inner);
        var progress = new ProgressLog();
        var delay = AfterFirstPlacement(launcher, sys, () =>
        {
            var w = Assert.Single(inner.Windows);
            inner.Windows[0] = w with { NormalRect = Target with { Height = 822 } };
        });

        var report = await new WorkspaceRestorer(sys, launcher, new SessionTracker(), delay)
            .RestoreAsync(Workspace(item), Settling(), progress);

        AssertFinalOnce(progress, item.Id, ItemOutcome.Launched);
        Assert.Single(report.Items);
    }

    [Fact]
    public async Task Duplicate_session_sibling_timeout_reports_each_final_once()
    {
        var inner = new FakeWindowSystem();
        var launcher = new FakeLauncher(inner);
        var inbox = Build.App(Chrome, "Inbox - Google Chrome", Target);
        var docs = Build.App(Chrome, "Docs - Google Chrome", Target with { X = 900 });
        inbox.App!.Arguments = docs.App!.Arguments = Profile;
        launcher.Opens[inbox.Id] = [(Chrome, "Inbox - Google Chrome", 1, Profile)];
        var progress = new ProgressLog();

        var report = await new WorkspaceRestorer(new ProbeWindows(inner), launcher, new SessionTracker(), launcher.Delay)
            .RestoreAsync(new Workspace { Items = [inbox, docs] }, Build.Fast(maxPolls: 4), progress);

        Assert.Equal([inbox.Id], launcher.Launched);
        Assert.Equal(ItemOutcome.Launched, report.Items.Single(r => r.ItemId == inbox.Id).Outcome);
        Assert.Equal(ItemOutcome.LaunchedNoWindow, report.Items.Single(r => r.ItemId == docs.Id).Outcome);
        AssertFinalOnce(progress, inbox.Id, ItemOutcome.Launched);
        AssertFinalOnce(progress, docs.Id, ItemOutcome.LaunchedNoWindow);
    }

    [Fact]
    public async Task Browser_launch_failure_reports_final_once()
    {
        var inner = new FakeWindowSystem();
        var launcher = new FakeLauncher(inner);
        var item = BrowserItem();
        launcher.Failing.Add(item.Id);
        var progress = new ProgressLog();

        var report = await Restorer(new ProbeWindows(inner), launcher)
            .RestoreAsync(Workspace(item), Build.Fast(), progress);

        Assert.Equal(ItemOutcome.LaunchFailed, report.Items.Single().Outcome);
        AssertFinalOnce(progress, item.Id, ItemOutcome.LaunchFailed);
    }

    [Fact]
    public async Task Browser_already_open_skipped_reports_final_once()
    {
        var inner = new FakeWindowSystem();
        inner.Add(Chrome, "Inbox - Google Chrome", args: Profile);
        var item = BrowserItem();
        var launcher = new FakeLauncher(inner);
        var progress = new ProgressLog();
        var options = new RestoreOptions
        {
            ReuseOpenWindows = false, // force the "browser is already open" skip path
            LaunchTimeout = TimeSpan.FromSeconds(1),
            PollInterval = TimeSpan.FromSeconds(1),
            SettleDelay = TimeSpan.Zero,
        };

        var report = await Restorer(new ProbeWindows(inner), launcher)
            .RestoreAsync(Workspace(item), options, progress);

        Assert.Empty(launcher.Launched);
        Assert.Equal(ItemOutcome.Skipped, report.Items.Single().Outcome);
        AssertFinalOnce(progress, item.Id, ItemOutcome.Skipped);
    }

    [Fact]
    public async Task Null_saved_window_skips_position_and_browser_verification()
    {
        var a = Arrange();
        a.Item.Window = null;

        var report = await Restorer(a.Sys, a.Launcher).RestoreAsync(Workspace(a.Item), Settling());

        var result = Assert.Single(report.Items);
        Assert.Equal(ItemOutcome.Launched, result.Outcome);
        Assert.Equal("No saved position", result.Detail);
        Assert.Equal(0, a.Sys.PlaceCount);
    }

    [Fact]
    public async Task Reused_chrome_window_is_not_final_verified()
    {
        var inner = new FakeWindowSystem();
        inner.Add(Chrome, "Inbox - Google Chrome", args: Profile);
        var item = Build.App(Chrome, "Inbox - Google Chrome", Target);
        item.App!.Arguments = Profile;
        var launcher = new FakeLauncher(inner);
        // The app moves itself immediately after the placement; a launched-only verifier must not fail a reuse.
        var sys = new ProbeWindows(inner, afterPlace: (h, r, s) =>
            inner.Place(h, new PixelRect(r.X, r.Y, r.Width, r.Height + 100), s));

        var report = await new WorkspaceRestorer(sys, launcher, new SessionTracker(), launcher.Delay)
            .RestoreAsync(Workspace(item), Settling());

        Assert.Equal(ItemOutcome.Reused, report.Items.Single().Outcome);
        Assert.Equal(1, sys.PlaceCount);
    }

    private static PixelRect ClampToMinimum(PixelRect r) =>
        new(r.X, r.Y, Math.Max(r.Width, ActualMin.Width), Math.Max(r.Height, ActualMin.Height));

    private static WorkspaceItem BrowserItem()
    {
        var item = Build.App(Chrome, "Inbox - Google Chrome", Target);
        item.App!.Arguments = Profile;
        return item;
    }

    private static (FakeWindowSystem Inner, FakeLauncher Launcher, ProbeWindows Sys, WorkspaceItem Item) Arrange(
        Func<PixelRect, PixelRect>? request = null)
    {
        var inner = new FakeWindowSystem();
        var launcher = new FakeLauncher(inner);
        var item = BrowserItem();
        launcher.Opens[item.Id] = [(Chrome, "Inbox - Google Chrome", 1, Profile)];
        return (inner, launcher, new ProbeWindows(inner, request: request), item);
    }

    private static Func<TimeSpan, CancellationToken, Task> AfterFirstPlacement(
        FakeLauncher launcher, ProbeWindows sys, Action action)
    {
        int postPlacementDelays = 0;
        return (TimeSpan _, CancellationToken ct) =>
        {
            var d = launcher.Delay(TimeSpan.Zero, ct);
            if (sys.PlaceCount > 0 && postPlacementDelays++ == 0) action();
            return d;
        };
    }

    private static WorkspaceRestorer Restorer(IWindowSystem sys, FakeLauncher launcher,
        Func<TimeSpan, CancellationToken, Task>? delay = null) =>
        new(sys, launcher, new SessionTracker(), delay ?? launcher.Delay);

    private static Workspace Workspace(WorkspaceItem item) => new() { Items = [item] };

    private static RestoreOptions Settling() => new()
    {
        LaunchTimeout = TimeSpan.FromSeconds(20),
        PollInterval = TimeSpan.FromSeconds(1),
        SettleDelay = TimeSpan.FromSeconds(1),
    };

    private static void AssertFinalOnce(ProgressLog progress, string itemId, ItemOutcome outcome)
    {
        var finals = progress.Items.Where(i => i.ItemId == itemId && i.Outcome != ItemOutcome.Pending).ToList();
        var single = Assert.Single(finals);
        Assert.Equal(outcome, single.Outcome);
    }

    private sealed class ProgressLog : IProgress<ItemResult>
    {
        public List<ItemResult> Items { get; } = [];
        public void Report(ItemResult value) => Items.Add(value);
    }

    /// <summary>Fake window system that can clamp requested rects and/or simulate an app moving itself right after Place.</summary>
    private sealed class ProbeWindows(
        FakeWindowSystem inner,
        Func<PixelRect, PixelRect>? request = null,
        Action<long, PixelRect, WindowShow>? afterPlace = null) : IWindowSystem
    {
        public int PlaceCount { get; private set; }
        public int OwnProcessId => inner.OwnProcessId;

        public IReadOnlyList<WindowInfo> GetAppWindows() => inner.GetAppWindows();
        public WindowInfo? Get(long handle) => inner.Get(handle);
        public bool IsAlive(long handle) => inner.IsAlive(handle);
        public IReadOnlyList<MonitorInfo> GetMonitors() => inner.GetMonitors();
        public (int L, int T, int R, int B) GetInvisibleBorders(long handle) => inner.GetInvisibleBorders(handle);
        public void BringToFront(long handle) => inner.BringToFront(handle);
        public bool Activate(long handle) => inner.Activate(handle);
        public void Close(long handle) => inner.Close(handle);
        public void Minimize(long handle) => inner.Minimize(handle);

        public PlaceOutcome Place(long handle, PixelRect normalRect, WindowShow show)
        {
            PlaceCount++;
            var outcome = inner.Place(handle, request?.Invoke(normalRect) ?? normalRect, show);
            if (outcome == PlaceOutcome.Ok) afterPlace?.Invoke(handle, normalRect, show);
            return outcome;
        }
    }
}
