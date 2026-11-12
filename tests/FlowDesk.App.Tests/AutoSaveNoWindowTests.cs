using FlowDesk.App.Autosave;
using FlowDesk.Core.Capture;
using FlowDesk.Core.Model;
using FlowDesk.Core.Restore;
using FlowDesk.Core.Windows;

namespace FlowDesk.App.Tests;

public sealed class AutoSaveNoWindowTests
{
    [Theory]
    [InlineData(0, false, false)]
    [InlineData(0, true, true)]
    [InlineData(2, false, true)]
    [InlineData(2, true, true)]
    public void WriteOnlyWhenPositionsChangedOrAPreviousWriteStillPends(
        int updatedCount, bool pendingPersistence, bool expected) =>
        Assert.Equal(expected, AutoSaveSavePolicy.ShouldWrite(updatedCount, pendingPersistence));

    [Fact]
    public async Task NoOpenWindowDoesNotWriteOrChangePositionsAndTime()
    {
        var savedAt = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var rect = new PixelRect(10, 20, 300, 200);
        var workspace = WorkspaceWithSavedWindow(savedAt, rect);
        var (coordinator, host, clock) = Create(workspace, pending: false);

        coordinator.Activate(workspace.Id, [], enabled: true, alwaysAdd: false, lastSavedAt: workspace.LayoutSavedAt);
        await coordinator.TickOnceAsync(); // first observation is pending
        clock.UtcNow += AutoSaveCoordinator.SettleDelay + TimeSpan.FromSeconds(1);
        await coordinator.TickOnceAsync();

        Assert.Equal(0, host.Writes);
        Assert.Equal(AutoSaveState.Watching, host.Statuses.Last().State);
        Assert.Equal("No open windows to save", host.Statuses.Last().Message);
        Assert.Single(host.Statuses, s => s.State == AutoSaveState.Saved); // activation only, no write
        Assert.Equal(savedAt, workspace.LayoutSavedAt);
        Assert.Equal(rect, workspace.Items[0].Window!.Rect);
    }

    [Fact]
    public async Task AWritePendingFromAFailedAddStillRetriesWithNoOpenWindow()
    {
        var workspace = WorkspaceWithSavedWindow(DateTimeOffset.Now, new PixelRect(10, 20, 300, 200));
        var (coordinator, host, clock) = Create(workspace, pending: true);

        coordinator.Activate(workspace.Id, [], enabled: true, alwaysAdd: false, lastSavedAt: workspace.LayoutSavedAt);
        await coordinator.TickOnceAsync();
        clock.UtcNow += AutoSaveCoordinator.SettleDelay + TimeSpan.FromSeconds(1);
        await coordinator.TickOnceAsync();

        Assert.Equal(1, host.Writes);
        Assert.Contains(host.Statuses, s => s.State == AutoSaveState.Saved);
    }

    private static Workspace WorkspaceWithSavedWindow(DateTimeOffset savedAt, PixelRect rect) => new()
    {
        Name = "Closed",
        LayoutSavedAt = savedAt,
        Items =
        [
            new WorkspaceItem
            {
                Kind = ItemKind.App, Name = "Notepad",
                App = new AppRef { ExePath = @"C:\Windows\notepad.exe" },
                Window = new SavedWindow
                {
                    Rect = rect, Show = WindowShow.Normal, MonitorDevice = "\\\\.\\DISPLAY1",
                    Title = "note", ClassName = "Notepad",
                },
            },
        ],
    };

    private static (AutoSaveCoordinator, CaptureHost, TestClock) Create(Workspace workspace, bool pending)
    {
        var clock = new TestClock();
        var host = new CaptureHost(workspace, pending);
        return (new AutoSaveCoordinator(host, clock), host, clock);
    }

    private sealed class TestClock : IAutoSaveClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        public Task DelayAsync(TimeSpan delay, CancellationToken ct) => Task.Delay(Timeout.InfiniteTimeSpan, ct);
    }

    /// <summary>Uses the real capture and save policy; only the app host's UI/persistence side is stubbed.</summary>
    private sealed class CaptureHost : IAutoSaveHost
    {
        private readonly WorkspaceCapture _capture;
        private readonly Workspace _workspace;
        public bool Pending { get; set; }
        public int Writes { get; private set; }
        public List<AutoSaveStatus> Statuses { get; } = [];

        public CaptureHost(Workspace workspace, bool pending)
        {
            _workspace = workspace;
            Pending = pending;
            _capture = new WorkspaceCapture(new EmptyWindows(), new SessionTracker());
        }

        public Task<WorkspaceObservation?> ObserveAsync(string workspaceId, CancellationToken ct) =>
            Task.FromResult<WorkspaceObservation?>(new WorkspaceObservation(workspaceId, "", []));

        public Task<bool?> SaveLayoutAsync(string workspaceId, long generation, Func<bool> isCurrent,
            CancellationToken ct)
        {
            var clone = _workspace.Clone();
            var updated = _capture.SaveLayout(clone);
            if (!AutoSaveSavePolicy.ShouldWrite(updated.Count, Pending)) return Task.FromResult<bool?>(null);
            Writes++;
            _workspace.LayoutSavedAt = clone.LayoutSavedAt;
            Pending = false;
            return Task.FromResult<bool?>(true);
        }

        public Task<AddWindowOutcome> AddWindowAsync(string workspaceId, long generation, long handle,
            Func<bool> isCurrent, CancellationToken ct) =>
            Task.FromResult(AddWindowOutcome.NotEligible);

        public void Publish(AutoSaveStatus status) => Statuses.Add(status);
        public void Offer(NewWindowOffer offer) { }
        public void DismissOffer(long generation) { }
    }

    private sealed class EmptyWindows : IWindowSystem
    {
        public int OwnProcessId => 999;
        public IReadOnlyList<WindowInfo> GetAppWindows() => [];
        public WindowInfo? Get(long handle) => null;
        public bool IsAlive(long handle) => false;
        public IReadOnlyList<MonitorInfo> GetMonitors() =>
            [new("\\\\.\\DISPLAY1", new(0, 0, 1920, 1080), new(0, 0, 1920, 1040), true, 96)];
        public PlaceOutcome Place(long handle, PixelRect normalRect, WindowShow show) => PlaceOutcome.WindowGone;
        public (int L, int T, int R, int B) GetInvisibleBorders(long handle) => (0, 0, 0, 0);
        public void BringToFront(long handle) { }
        public bool Activate(long handle) => false;
        public void Close(long handle) { }
        public void Minimize(long handle) { }
    }
}
