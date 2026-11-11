using FlowDesk.App.Autosave;

namespace FlowDesk.App.Tests;

public sealed class AutoSaveCoordinatorTests
{
    private static readonly TimeSpan Poll = AutoSaveCoordinator.PollInterval;

    [Fact]
    public async Task SavesOnlyAfterPlacementChangesSettle()
    {
        var (coordinator, host, clock) = Create();
        host.Observation = _ => Obs("ws", "A");
        coordinator.Activate("ws", [], enabled: true, alwaysAdd: false);
        await coordinator.TickOnceAsync();

        clock.UtcNow += Poll;
        host.Observation = _ => Obs("ws", "B");
        await coordinator.TickOnceAsync();
        clock.UtcNow += Poll;
        await coordinator.TickOnceAsync();
        Assert.Empty(host.Saves);

        clock.UtcNow += TimeSpan.FromMilliseconds(1100);
        await coordinator.TickOnceAsync();
        Assert.Equal(["ws"], host.Saves);
        Assert.Contains(host.Statuses, s => s.WorkspaceId == "ws" && s.State == AutoSaveState.Saved);
    }

    [Fact]
    public async Task FirstObservationIsSavedAfterSettleWithoutAnotherChange()
    {
        var (coordinator, host, clock) = Create();
        host.Observation = _ => Obs("ws", "A");
        coordinator.Activate("ws", [], enabled: true, alwaysAdd: false);
        await coordinator.TickOnceAsync();
        Assert.Empty(host.Saves);

        clock.UtcNow += TimeSpan.FromSeconds(4);
        await coordinator.TickOnceAsync();
        Assert.Single(host.Saves);
    }

    [Fact]
    public async Task AnotherChangeRestartsTheSettleWindow()
    {
        var (coordinator, host, clock) = Create();
        host.Observation = _ => Obs("ws", "A");
        coordinator.Activate("ws", [], enabled: true, alwaysAdd: false);
        await coordinator.TickOnceAsync();

        clock.UtcNow += Poll;
        host.Observation = _ => Obs("ws", "B");
        await coordinator.TickOnceAsync();
        clock.UtcNow += Poll;
        host.Observation = _ => Obs("ws", "C");
        await coordinator.TickOnceAsync();
        clock.UtcNow += Poll;
        await coordinator.TickOnceAsync();
        Assert.Empty(host.Saves);

        clock.UtcNow += TimeSpan.FromMilliseconds(1100);
        await coordinator.TickOnceAsync();
        Assert.Single(host.Saves);
    }

    [Fact]
    public async Task SwitchingCancelsThePendingSaveOfTheOriginalWorkspace()
    {
        var (coordinator, host, clock) = Create();
        host.Observation = id => Obs(id, id == "a" ? "A-live" : "B-live");
        coordinator.Activate("a", [], enabled: true, alwaysAdd: false);
        await coordinator.TickOnceAsync();

        clock.UtcNow += Poll;
        host.Observation = id => Obs(id, "A-changed");
        await coordinator.TickOnceAsync();
        coordinator.Activate("b", [], enabled: true, alwaysAdd: false);

        clock.UtcNow += TimeSpan.FromSeconds(5);
        host.Observation = id => Obs(id, id == "a" ? "A-changed" : "B-live");
        await coordinator.TickOnceAsync();
        await coordinator.TickOnceAsync();
        Assert.DoesNotContain("a", host.Saves);
        Assert.DoesNotContain(host.Statuses, s => s.WorkspaceId == "a" && s.State == AutoSaveState.Saved);
    }

    [Fact]
    public async Task SwitchingWhileSavingDoesNotPublishSavedForTheNewWorkspace()
    {
        var (coordinator, host, clock) = Create();
        host.Observation = _ => Obs("a", "A");
        coordinator.Activate("a", [], enabled: true, alwaysAdd: false);
        await coordinator.TickOnceAsync();
        clock.UtcNow += Poll;
        host.Observation = _ => Obs("a", "A2");
        await coordinator.TickOnceAsync();
        clock.UtcNow += TimeSpan.FromSeconds(4);
        host.SaveGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var saving = coordinator.TickOnceAsync();
        await host.SaveStarted.Task;

        coordinator.Activate("b", [], enabled: true, alwaysAdd: false);
        host.SaveGate.SetResult();
        await saving;

        Assert.DoesNotContain(host.Statuses, s => s.WorkspaceId == "b" && s.State == AutoSaveState.Saved);
    }

    [Fact]
    public async Task SameIdReactivationRejectsTheStaleSaveContinuation()
    {
        var (coordinator, host, clock) = Create();
        host.Observation = _ => Obs("ws", "A");
        coordinator.Activate("ws", [], enabled: true, alwaysAdd: false);
        await coordinator.TickOnceAsync();
        clock.UtcNow += TimeSpan.FromSeconds(4);
        host.SaveGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var saving = coordinator.TickOnceAsync();
        await host.SaveStarted.Task;
        int statusesBefore = host.Statuses.Count;

        coordinator.Activate("ws", [], enabled: true, alwaysAdd: false); // same id, new generation
        Assert.False(host.LastSaveIsCurrent!());
        host.SaveGate.SetResult();
        await saving;

        Assert.DoesNotContain(host.Statuses.Skip(statusesBefore), s => s.State == AutoSaveState.Saved);
    }

    [Fact]
    public async Task DisabledDoesNotObserveOrSaveAndCanBeReenabled()
    {
        var (coordinator, host, clock) = Create();
        host.Observation = _ => Obs("ws", "A");
        coordinator.Activate("ws", [], enabled: false, alwaysAdd: false);
        clock.UtcNow += Poll;
        await coordinator.TickOnceAsync();
        Assert.Equal(0, host.ObserveCount);

        coordinator.SetEnabled(true, []);
        await coordinator.TickOnceAsync();
        clock.UtcNow += Poll;
        host.Observation = _ => Obs("ws", "B");
        await coordinator.TickOnceAsync();
        clock.UtcNow += TimeSpan.FromSeconds(4);
        await coordinator.TickOnceAsync();
        Assert.Single(host.Saves);
    }

    [Fact]
    public async Task SuspendAwaitsInFlightWorkAndInvalidatesTheGeneration()
    {
        var (coordinator, host, _) = Create();
        host.Observation = _ => Obs("ws", "A");
        coordinator.Activate("ws", [], enabled: true, alwaysAdd: false);
        host.ObserveGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var ticking = coordinator.TickOnceAsync();
        await host.ObserveStarted.Task;

        var suspend = coordinator.SuspendAsync();
        await Task.Delay(20);
        Assert.False(suspend.IsCompleted);
        host.ObserveGate.SetResult();
        await ticking;
        await suspend;
        Assert.False(coordinator.IsCurrent("ws", 0));
    }

    [Fact]
    public async Task ResumePreservesBaselineAndNotNowSuppression()
    {
        var (coordinator, host, _) = Create();
        host.Observation = _ => Obs("ws", "A", Win(3), Win(7, name: "Nope"));
        coordinator.Activate("ws", [3], enabled: true, alwaysAdd: false);
        await coordinator.TickOnceAsync();
        var offer = host.Offers.Single();

        coordinator.Decline(offer.Handle, offer.Generation);
        await coordinator.SuspendAsync();
        coordinator.Resume(enabled: true, alwaysAdd: false);
        Assert.True(host.DismissedGenerations.Last() > offer.Generation);
        int offersBefore = host.Offers.Count;

        host.Observation = _ => Obs("ws", "A", Win(3), Win(7, name: "Nope"), Win(8, name: "New"));
        await coordinator.TickOnceAsync();
        Assert.DoesNotContain(host.Offers.Skip(offersBefore), o => o.Handle == 7);
        Assert.Contains(host.Offers.Skip(offersBefore), o => o.Handle == 8);
    }

    [Fact]
    public async Task ResumePreservesPendingSettleWork()
    {
        var (coordinator, host, clock) = Create();
        host.Observation = _ => Obs("ws", "A");
        coordinator.Activate("ws", [], enabled: true, alwaysAdd: false);
        await coordinator.TickOnceAsync(); // first observation is pending
        await coordinator.SuspendAsync();
        coordinator.Resume(enabled: true, alwaysAdd: false);

        clock.UtcNow += TimeSpan.FromSeconds(4);
        await coordinator.TickOnceAsync();
        Assert.Single(host.Saves);
    }

    [Fact]
    public async Task AddPersistenceFailureForcesARetryEvenWhenNothingChanged()
    {
        var (coordinator, host, clock) = Create();
        host.AddResults.Enqueue(AddWindowOutcome.PersistenceFailed);
        host.Observation = _ => Obs("ws", "A", Win(8, name: "Spotify"));
        coordinator.Activate("ws", [], enabled: true, alwaysAdd: false);
        await coordinator.TickOnceAsync();
        var offer = host.Offers.Single();
        Assert.Equal(AddWindowOutcome.PersistenceFailed, await coordinator.AcceptAsync(offer.Handle, offer.Generation));
        Assert.Empty(host.Saves);

        host.Observation = _ => Obs("ws", "A"); // window closed; layout signature unchanged
        clock.UtcNow += Poll;
        await coordinator.TickOnceAsync();
        Assert.Single(host.Saves);
        Assert.Equal(AutoSaveState.Saved, host.Statuses.Last().State);
    }

    [Fact]
    public async Task SuspendAwaitsInFlightSaveBeforeResuming()
    {
        var (coordinator, host, clock) = Create();
        host.Observation = _ => Obs("ws", "A");
        coordinator.Activate("ws", [], enabled: true, alwaysAdd: false);
        await coordinator.TickOnceAsync();
        clock.UtcNow += TimeSpan.FromSeconds(4);
        host.SaveGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var saving = coordinator.TickOnceAsync();
        await host.SaveStarted.Task;

        var suspend = coordinator.SuspendAsync();
        await Task.Delay(20);
        Assert.False(suspend.IsCompleted);
        host.SaveGate.SetResult();
        await saving;
        await suspend;
        coordinator.Resume(enabled: true, alwaysAdd: false);
        Assert.False(coordinator.IsSuspended);
    }

    [Fact]
    public async Task FailedSaveIsRetriedOnTheNextTick()
    {
        var (coordinator, host, clock) = Create();
        host.SaveResults.Enqueue(false);
        host.SaveResults.Enqueue(true);
        host.Observation = _ => Obs("ws", "A");
        coordinator.Activate("ws", [], enabled: true, alwaysAdd: false);
        await coordinator.TickOnceAsync();

        clock.UtcNow += TimeSpan.FromSeconds(4);
        await coordinator.TickOnceAsync();
        Assert.Single(host.Saves);
        Assert.Contains(host.Statuses, s => s.State == AutoSaveState.Failed);

        clock.UtcNow += Poll;
        await coordinator.TickOnceAsync();
        Assert.Equal(2, host.Saves.Count);
        Assert.Equal(AutoSaveState.Saved, host.Statuses.Last().State);
    }

    [Fact]
    public async Task ConcurrentTickReturnsInsteadOfOverlapping()
    {
        var (coordinator, host, _) = Create();
        host.Observation = _ => Obs("ws", "A");
        coordinator.Activate("ws", [], enabled: true, alwaysAdd: false);
        host.ObserveGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = coordinator.TickOnceAsync();
        await host.ObserveStarted.Task;

        await coordinator.TickOnceAsync();
        Assert.Equal(1, host.ObserveCount);

        host.ObserveGate.SetResult();
        await first;
    }

    [Fact]
    public async Task BaselineBoundMatchingReplacementOwnAndNonCandidatesAreNotOffered()
    {
        var (coordinator, host, _) = Create();
        host.Observation = _ => Obs("ws", "A",
            Win(1, bound: true), Win(2, missing: true), Win(3), Win(4), Win(5, own: true));
        coordinator.Activate("ws", [3, 4], enabled: true, alwaysAdd: false);
        await coordinator.TickOnceAsync();
        Assert.Empty(host.Offers);

        host.Observation = _ => Obs("ws", "B",
            Win(1, bound: true), Win(2, missing: true), Win(3), Win(4), Win(5, own: true), Win(6, name: "Spotify"));
        await coordinator.TickOnceAsync();
        Assert.Single(host.Offers);
        Assert.Equal(6, host.Offers[0].Handle);
        Assert.Equal("Spotify", host.Offers[0].Name);
    }

    [Fact]
    public async Task TwoCandidatesMatchingOneMissingItemAreBothNotOffered()
    {
        var (coordinator, host, _) = Create();
        host.Observation = _ => Obs("ws", "A", Win(10, missing: true), Win(11, missing: true));
        coordinator.Activate("ws", [], enabled: true, alwaysAdd: false);
        await coordinator.TickOnceAsync();
        Assert.Empty(host.Offers);
    }

    [Fact]
    public async Task NotNowSuppressesRepeatedOffersForThatWindow()
    {
        var (coordinator, host, _) = Create();
        host.Observation = _ => Obs("ws", "A", Win(7, name: "Spotify"));
        coordinator.Activate("ws", [], enabled: true, alwaysAdd: false);
        await coordinator.TickOnceAsync();
        var offer = host.Offers.Single();

        coordinator.Decline(offer.Handle, offer.Generation);
        await coordinator.TickOnceAsync();
        Assert.Single(host.Offers);
        Assert.Empty(host.Adds);
    }

    [Fact]
    public async Task StaleOfferGenerationDoesNotAffectAReactivation()
    {
        var (coordinator, host, _) = Create();
        host.Observation = _ => Obs("ws", "A", Win(9, name: "Old"));
        coordinator.Activate("ws", [], enabled: true, alwaysAdd: false);
        await coordinator.TickOnceAsync();
        var old = host.Offers.Single();

        coordinator.Activate("ws", [], enabled: true, alwaysAdd: false);
        host.Observation = _ => Obs("ws", "A", Win(10, name: "New"));
        await coordinator.TickOnceAsync();
        var fresh = host.Offers.Last();
        Assert.NotEqual(old.Generation, fresh.Generation);

        coordinator.Decline(old.Handle, old.Generation);
        Assert.Equal(AddWindowOutcome.NotEligible, await coordinator.AcceptAsync(old.Handle, old.Generation));
        await coordinator.TickOnceAsync();
        Assert.Equal(fresh, host.Offers.Last());
    }

    [Fact]
    public async Task AlwaysAddsNewWindowsWithoutOffering()
    {
        var (coordinator, host, _) = Create();
        host.Observation = _ => Obs("ws", "A", Win(8, name: "Spotify"));
        coordinator.Activate("ws", [], enabled: true, alwaysAdd: true);
        await coordinator.TickOnceAsync();
        Assert.Empty(host.Offers);
        Assert.Equal(("ws", 8L), host.Adds.Single());

        await coordinator.TickOnceAsync();
        Assert.Single(host.Adds);
    }

    [Fact]
    public async Task AddPersistenceFailureIsReportedAsRetryNotMissing()
    {
        var (coordinator, host, _) = Create();
        host.AddResults.Enqueue(AddWindowOutcome.PersistenceFailed);
        host.Observation = _ => Obs("ws", "A", Win(8, name: "Spotify"));
        coordinator.Activate("ws", [], enabled: true, alwaysAdd: false);
        await coordinator.TickOnceAsync();
        var offer = host.Offers.Single();

        var outcome = await coordinator.AcceptAsync(offer.Handle, offer.Generation);
        Assert.Equal(AddWindowOutcome.PersistenceFailed, outcome);
        Assert.Contains(host.Statuses, s => s.State == AutoSaveState.Failed && s.Message!.Contains("could not be saved"));
    }

    [Fact]
    public async Task SwitchDismissesStaleOfferAndOffersOnlyForTheNewWorkspace()
    {
        var (coordinator, host, _) = Create();
        host.Observation = id => id == "a" ? Obs("a", "A", Win(9, name: "A app")) : Obs("b", "B");
        coordinator.Activate("a", [], enabled: true, alwaysAdd: false);
        await coordinator.TickOnceAsync();
        Assert.Equal("a", host.Offers.Single().WorkspaceId);

        coordinator.Activate("b", [], enabled: true, alwaysAdd: false);
        Assert.True(host.Dismissed >= 1);
        host.Observation = _ => Obs("b", "B", Win(10, name: "B app"));
        await coordinator.TickOnceAsync();
        Assert.Equal("b", host.Offers.Last().WorkspaceId);
        Assert.Equal(10, host.Offers.Last().Handle);
    }

    [Fact]
    public async Task StopCompletesTheLoopCleanly()
    {
        var (coordinator, _, _) = Create();
        coordinator.Start();
        await Task.Delay(20);
        await coordinator.StopAsync().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(true);
    }

    private static (AutoSaveCoordinator Coordinator, FakeHost Host, TestClock Clock) Create()
    {
        var clock = new TestClock();
        var host = new FakeHost();
        return (new AutoSaveCoordinator(host, clock), host, clock);
    }

    private static WorkspaceObservation Obs(string id, string signature, params ObservedWindow[] windows) =>
        new(id, signature, windows);

    private static ObservedWindow Win(long handle, string name = "App", bool own = false, bool bound = false,
        bool missing = false) => new(handle, name, own, bound, missing);

    private sealed class TestClock : IAutoSaveClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        public Task DelayAsync(TimeSpan delay, CancellationToken ct) => Task.Delay(Timeout.InfiniteTimeSpan, ct);
    }

    private sealed class FakeHost : IAutoSaveHost
    {
        public Func<string, WorkspaceObservation?> Observation { get; set; } = _ => null;
        public Queue<bool?> SaveResults { get; } = new();
        public Queue<AddWindowOutcome> AddResults { get; } = new();
        public List<string> Saves { get; } = [];
        public List<(string WorkspaceId, long Handle)> Adds { get; } = [];
        public List<NewWindowOffer> Offers { get; } = [];
        public List<AutoSaveStatus> Statuses { get; } = [];
        public int Dismissed { get; private set; }
        public List<long> DismissedGenerations { get; } = [];
        public int ObserveCount { get; private set; }
        public Func<bool>? LastSaveIsCurrent { get; private set; }
        public TaskCompletionSource ObserveStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SaveStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource? ObserveGate { get; set; }
        public TaskCompletionSource? SaveGate { get; set; }

        public async Task<WorkspaceObservation?> ObserveAsync(string workspaceId, CancellationToken ct)
        {
            ObserveCount++;
            ObserveStarted.TrySetResult();
            if (ObserveGate is not null) await ObserveGate.Task;
            return Observation(workspaceId);
        }

        public async Task<bool?> SaveLayoutAsync(string workspaceId, long generation, Func<bool> isCurrent,
            CancellationToken ct)
        {
            Saves.Add(workspaceId);
            LastSaveIsCurrent = isCurrent;
            SaveStarted.TrySetResult();
            if (SaveGate is not null) await SaveGate.Task;
            return SaveResults.Count > 0 ? SaveResults.Dequeue() : true;
        }

        public Task<AddWindowOutcome> AddWindowAsync(string workspaceId, long generation, long handle,
            Func<bool> isCurrent, CancellationToken ct)
        {
            Adds.Add((workspaceId, handle));
            return Task.FromResult(AddResults.Count > 0 ? AddResults.Dequeue() : AddWindowOutcome.Added);
        }

        public void Publish(AutoSaveStatus status) => Statuses.Add(status);
        public void Offer(NewWindowOffer offer) => Offers.Add(offer);
        public void DismissOffer(long generation) { Dismissed++; DismissedGenerations.Add(generation); }
    }
}
