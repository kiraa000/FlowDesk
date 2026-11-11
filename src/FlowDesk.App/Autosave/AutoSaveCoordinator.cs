namespace FlowDesk.App.Autosave;

public enum AutoSaveState { Off, Watching, Pending, Saved, Failed }

/// <summary>A cheap, stable description of one open window for the new-window gate.</summary>
public sealed record ObservedWindow(
    long Handle,
    string Name,
    bool IsOwnProcess,
    bool IsBoundToItem,
    bool MatchesMissingLiveItem);

/// <summary>One background read of the active workspace. PlacementSignature must include bound handles.</summary>
public sealed record WorkspaceObservation(
    string WorkspaceId,
    string PlacementSignature,
    IReadOnlyList<ObservedWindow> Windows);

public sealed record NewWindowOffer(string WorkspaceId, long Handle, string Name, long Generation);

public sealed record AutoSaveStatus(
    string WorkspaceId, AutoSaveState State, DateTimeOffset? SavedAt, string? Message = null, long Generation = 0);

public enum AddWindowOutcome
{
    /// <summary>The window became a workspace item and the item was persisted.</summary>
    Added,
    /// <summary>The window closed, is bound, or is now a replacement for a missing item.</summary>
    NotEligible,
    /// <summary>The item was added in memory, but its write failed; autosave will retry the write.</summary>
    PersistenceFailed,
}

public interface IAutoSaveHost
{
    /// <summary>Called off the UI thread. Returns null if the workspace is gone.</summary>
    Task<WorkspaceObservation?> ObserveAsync(string workspaceId, CancellationToken ct);
    /// <summary>Called off the UI thread. true = persisted, false = failed, null = nothing needed saving.</summary>
    Task<bool?> SaveLayoutAsync(string workspaceId, long generation, Func<bool> isCurrent, CancellationToken ct);
    /// <summary>Called off the UI thread. Re-checks live state and generation before committing on the UI thread.</summary>
    Task<AddWindowOutcome> AddWindowAsync(string workspaceId, long generation, long handle,
        Func<bool> isCurrent, CancellationToken ct);
    void Publish(AutoSaveStatus status);
    void Offer(NewWindowOffer offer);
    void DismissOffer(long generation);
}

public interface IAutoSaveClock
{
    DateTimeOffset UtcNow { get; }
    Task DelayAsync(TimeSpan delay, CancellationToken ct);
}

public sealed class SystemAutoSaveClock : IAutoSaveClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    public Task DelayAsync(TimeSpan delay, CancellationToken ct) => Task.Delay(delay, ct);
}

/// <summary>
/// Watches the active workspace off the UI thread: polls, waits for placement changes to settle, saves and
/// offers newly opened windows. One tick/save/add runs at a time. Every activation has a generation epoch;
/// suspend/reactivate invalidates queued observations and UI commits, including a same-id reactivation.
/// </summary>
public sealed class AutoSaveCoordinator
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan SettleDelay = TimeSpan.FromSeconds(3);

    private readonly IAutoSaveHost _host;
    private readonly IAutoSaveClock _clock;
    private readonly Func<bool> _isBusy;
    private readonly SemaphoreSlim _work = new(1, 1);
    private readonly object _sync = new();

    private CancellationTokenSource? _loopCts;
    private CancellationTokenSource? _activationCts;
    private Task? _loop;

    private string? _activeId;
    private long _generation;
    private bool _suspended;
    private HashSet<long> _baseline = [];
    private readonly HashSet<long> _declined = [];
    private readonly HashSet<long> _handled = [];
    private long? _offerHandle;
    private bool _enabled;
    private bool _alwaysAdd;
    private string? _lastSignature;
    private DateTimeOffset? _changedAt;
    private DateTimeOffset? _lastSavedAt;
    private bool _failed;

    public AutoSaveCoordinator(IAutoSaveHost host, IAutoSaveClock clock, Func<bool>? isBusy = null)
    {
        _host = host;
        _clock = clock;
        _isBusy = isBusy ?? (() => false);
    }

    public string? ActiveWorkspaceId { get { lock (_sync) return _activeId; } }

    public void Start()
    {
        lock (_sync)
        {
            if (_loop is not null) return;
            // StopAsync invalidated the old epoch/token; a restart (e.g. a cancelled close) gets a fresh one.
            if (_activeId is not null && _enabled)
            {
                _generation++;
                _suspended = false;
                _activationCts = new CancellationTokenSource();
            }
            _loopCts = new CancellationTokenSource();
            _loop = RunAsync(_loopCts.Token);
        }
    }

    public async Task StopAsync()
    {
        Task? loop;
        CancellationTokenSource? cts;
        long generation;
        lock (_sync)
        {
            cts = _loopCts; _loopCts = null; loop = _loop; _loop = null;
            _generation++;
            generation = _generation;
            _suspended = true;
            _activationCts?.Cancel();
            _offerHandle = null;
        }
        cts?.Cancel();
        _host.DismissOffer(generation);
        if (loop is not null)
        {
            try { await loop.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (Exception) { /* shutting down; nothing useful to report */ }
        }
        // Also wait for an in-flight Add accepted from the InfoBar, so no model/save work outlives shutdown.
        await _work.WaitAsync().ConfigureAwait(false);
        _work.Release();
        cts?.Dispose();
    }

    /// <summary>
    /// Cancels the current epoch and waits until no poll/save/add is still running. Call before a core switch,
    /// manual save, layout apply or other operation that mutates the tracker or window placement.
    /// </summary>
    public async Task SuspendAsync()
    {
        long generation;
        lock (_sync)
        {
            _suspended = true;
            _generation++;
            generation = _generation;
            _activationCts?.Cancel();
            _offerHandle = null;
        }
        _host.DismissOffer(generation);
        await _work.WaitAsync().ConfigureAwait(false);
        _work.Release();
    }

    /// <summary>Starts watching a workspace. Baseline handles are the windows that existed when it was activated.</summary>
    public void Activate(string workspaceId, IEnumerable<long> baselineHandles, bool enabled, bool alwaysAdd,
        DateTimeOffset? lastSavedAt = null)
    {
        AutoSaveState state;
        long generation;
        lock (_sync)
        {
            _activationCts?.Cancel();
            _activationCts = new CancellationTokenSource();
            _generation++;
            generation = _generation;
            _suspended = false;
            _activeId = workspaceId;
            _baseline = baselineHandles.ToHashSet();
            _declined.Clear();
            _handled.Clear();
            _offerHandle = null;
            _enabled = enabled;
            _alwaysAdd = alwaysAdd;
            _lastSignature = null;
            _changedAt = null;
            _lastSavedAt = lastSavedAt;
            _failed = false;
            state = !enabled ? AutoSaveState.Off : lastSavedAt is not null ? AutoSaveState.Saved : AutoSaveState.Watching;
        }
        _host.DismissOffer(generation);
        Publish(state, null);
    }

    /// <summary>
    /// Resumes the SAME activation after a quiesced UI operation. A new epoch/token keeps queued work stale,
    /// but baseline, Not-now/handled handles and settle/failure state are preserved. No pending offer is kept.
    /// </summary>
    public void Resume(bool enabled, bool alwaysAdd)
    {
        AutoSaveState state;
        string? message = null;
        long generation;
        lock (_sync)
        {
            if (_activeId is null) return;
            _generation++;
            generation = _generation;
            _activationCts?.Cancel();
            _activationCts = new CancellationTokenSource();
            _suspended = false;
            _enabled = enabled;
            _alwaysAdd = alwaysAdd;
            _offerHandle = null;
            state = !enabled ? AutoSaveState.Off
                : _failed ? AutoSaveState.Failed
                : _changedAt is not null ? AutoSaveState.Pending
                : _lastSavedAt is not null ? AutoSaveState.Saved
                : AutoSaveState.Watching;
            if (_failed) message = "Window positions could not be saved. Autosave will retry.";
        }
        _host.DismissOffer(generation);
        Publish(state, message);
    }

    /// <summary>Stops watching (workspace deleted or app shutting down).</summary>
    public void Deactivate()
    {
        long generation;
        lock (_sync)
        {
            _generation++;
            generation = _generation;
            _suspended = true;
            _activationCts?.Cancel();
            _activeId = null; _enabled = false; _baseline.Clear();
            _declined.Clear(); _handled.Clear();
            _offerHandle = null;
            _lastSignature = null; _changedAt = null; _failed = false;
        }
        _host.DismissOffer(generation);
    }

    public void SetEnabled(bool enabled, IEnumerable<long>? freshBaseline = null)
    {
        AutoSaveState state;
        long generation;
        lock (_sync)
        {
            _generation++;
            generation = _generation;
            _enabled = enabled;
            _suspended = !enabled;
            _activationCts?.Cancel();
            if (enabled)
            {
                _activationCts = new CancellationTokenSource();
                if (freshBaseline is not null) _baseline = freshBaseline.ToHashSet();
                _lastSignature = null; _changedAt = null; _failed = false;
                _declined.Clear(); _handled.Clear();
                _offerHandle = null;
            }
            else
            {
                _offerHandle = null;
            }
            state = enabled
                ? _lastSavedAt is not null ? AutoSaveState.Saved : AutoSaveState.Watching
                : AutoSaveState.Off;
        }
        _host.DismissOffer(generation);
        Publish(state, null);
    }

    public void SetAlwaysAdd(bool always)
    {
        lock (_sync) _alwaysAdd = always;
    }

    public bool IsSuspended { get { lock (_sync) return _suspended; } }

    public long Generation { get { lock (_sync) return _generation; } }

    public bool IsCurrent(string workspaceId, long generation)
    {
        lock (_sync) return IsCurrentLocked(workspaceId, generation);
    }

    /// <summary>True for the current epoch even when autosave is disabled (used to accept a current Off status).</summary>
    public bool IsCurrentGeneration(string workspaceId, long generation)
    {
        lock (_sync) return _activeId == workspaceId && _generation == generation;
    }

    /// <summary>One poll/decision. Concurrent calls return immediately so polls never overlap.</summary>
    public async Task TickOnceAsync(CancellationToken ct = default)
    {
        if (!await _work.WaitAsync(0, ct).ConfigureAwait(false)) return;
        try
        {
            if (_isBusy()) return;
            string id;
            long generation;
            CancellationToken activation;
            lock (_sync)
            {
                if (_activeId is null || !_enabled || _suspended) return;
                id = _activeId; generation = _generation;
                activation = _activationCts?.Token ?? ct;
            }
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, activation);
            var observation = await _host.ObserveAsync(id, linked.Token).ConfigureAwait(false);
            if (observation is null || observation.WorkspaceId != id || !IsCurrent(id, generation)) return;

            await OfferNewWindowsAsync(id, generation, observation, linked.Token).ConfigureAwait(false);
            if (!IsCurrent(id, generation)) return;
            await SaveWhenSettledAsync(id, generation, observation, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        finally { _work.Release(); }
    }

    /// <summary>User chose Add for the offered window.</summary>
    public async Task<AddWindowOutcome> AcceptAsync(long handle, long generation, CancellationToken ct = default)
    {
        await _work.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            string id;
            CancellationToken activation;
            lock (_sync)
            {
                if (!IsCurrentLockedOffer(handle, generation)) return AddWindowOutcome.NotEligible;
                id = _activeId!;
                activation = _activationCts?.Token ?? ct;
            }
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, activation);
            var outcome = await _host.AddWindowAsync(id, generation, handle,
                () => IsCurrent(id, generation), linked.Token).ConfigureAwait(false);
            if (!IsCurrent(id, generation)) return outcome; // a later activation owns the offer/status now

            lock (_sync)
            {
                if (_offerHandle == handle) { _offerHandle = null; }
                if (outcome is AddWindowOutcome.Added or AddWindowOutcome.PersistenceFailed)
                {
                    _handled.Add(handle); _declined.Add(handle);
                }
                else _declined.Add(handle);
                if (outcome == AddWindowOutcome.PersistenceFailed) _failed = true;
            }
            _host.DismissOffer(generation);
            if (outcome == AddWindowOutcome.PersistenceFailed)
                PublishCurrent(id, generation, AutoSaveState.Failed,
                    "The window was added, but its workspace item could not be saved. Autosave will retry.");
            return outcome;
        }
        finally { _work.Release(); }
    }

    /// <summary>User chose Not now: suppress this window for the rest of this activation.</summary>
    public void Decline(long handle, long generation)
    {
        lock (_sync)
        {
            if (!IsCurrentLockedOffer(handle, generation)) return;
            _declined.Add(handle);
            _offerHandle = null;
        }
        _host.DismissOffer(generation);
    }

    private async Task OfferNewWindowsAsync(string id, long generation, WorkspaceObservation observation, CancellationToken ct)
    {
        long? pending;
        bool always;
        HashSet<long> baseline, declined, handled;
        lock (_sync)
        {
            if (!IsCurrentLocked(id, generation)) return;
            pending = _offerHandle;
            always = _alwaysAdd;
            baseline = [.. _baseline];
            declined = [.. _declined];
            handled = [.. _handled];
        }

        bool StillCandidate(ObservedWindow w) =>
            !w.IsOwnProcess && !w.IsBoundToItem && !w.MatchesMissingLiveItem
            && !baseline.Contains(w.Handle) && !declined.Contains(w.Handle) && !handled.Contains(w.Handle);

        var candidates = observation.Windows.Where(w => w.Handle != pending).Where(StillCandidate).ToList();

        if (pending is { } stillOffered)
        {
            bool present = observation.Windows.Any(w => w.Handle == stillOffered && StillCandidate(w));
            if (!present)
            {
                lock (_sync) { if (IsCurrentLocked(id, generation) && _offerHandle == stillOffered) _offerHandle = null; }
                if (IsCurrent(id, generation)) _host.DismissOffer(generation);
                pending = null;
            }
        }

        if (always)
        {
            foreach (var candidate in candidates)
            {
                if (!IsCurrent(id, generation)) return;
                var outcome = await _host.AddWindowAsync(id, generation, candidate.Handle,
                    () => IsCurrent(id, generation), ct).ConfigureAwait(false);
                if (!IsCurrent(id, generation)) return;
                lock (_sync)
                {
                    _handled.Add(candidate.Handle);
                    _declined.Add(candidate.Handle);
                }
                if (outcome == AddWindowOutcome.PersistenceFailed)
                {
                    lock (_sync) _failed = true;
                    PublishCurrent(id, generation, AutoSaveState.Failed,
                        "A window was added, but its workspace item could not be saved. Autosave will retry.");
                }
            }
            return;
        }

        if (pending is not null || candidates.Count == 0) return;
        var offer = candidates[0];
        lock (_sync)
        {
            if (!IsCurrentLocked(id, generation)) return;
            _offerHandle = offer.Handle;
        }
        _host.Offer(new NewWindowOffer(id, offer.Handle, offer.Name, generation));
    }

    private async Task SaveWhenSettledAsync(string id, long generation, WorkspaceObservation observation, CancellationToken ct)
    {
        string signature = observation.PlacementSignature;
        var now = _clock.UtcNow;
        bool retry = false;
        bool newlyPending = false;
        lock (_sync)
        {
            if (!IsCurrentLocked(id, generation)) return;
            if (_lastSignature is null)
            {
                // Capture the first observation as "changed now": movement between Activate and the first poll
                // is saved after the settle window instead of being swallowed as a baseline.
                _lastSignature = signature;
                _changedAt = now;
                newlyPending = true;
            }
            else
            {
                retry = _failed;
                if (signature != _lastSignature)
                {
                    _lastSignature = signature;
                    _changedAt = now;
                    _failed = false;
                    newlyPending = true;
                }
                else if (!retry && (_changedAt is null || now - _changedAt < SettleDelay)) return;
            }
        }
        if (newlyPending)
        {
            PublishCurrent(id, generation, AutoSaveState.Pending, null);
            return;
        }

        bool? result = await _host.SaveLayoutAsync(id, generation,
            () => IsCurrent(id, generation), ct).ConfigureAwait(false);
        AutoSaveState state;
        string? message;
        lock (_sync)
        {
            if (!IsCurrentLocked(id, generation)) return;
            if (result is true)
            {
                _lastSavedAt = now;
                _changedAt = null;
                _failed = false;
                state = AutoSaveState.Saved; message = null;
            }
            else if (result is null)
            {
                _changedAt = null;
                _failed = false;
                state = AutoSaveState.Watching; message = "No open windows to save";
            }
            else
            {
                _failed = true;
                state = AutoSaveState.Failed;
                message = "Window positions could not be saved. Autosave will retry.";
            }
        }
        PublishCurrent(id, generation, state, message);
    }

    private bool IsCurrentLocked(string id, long generation) =>
        _activeId == id && _generation == generation && !_suspended && _enabled;

    private bool IsCurrentLockedOffer(long handle, long generation) =>
        _activeId is not null && IsCurrentLocked(_activeId, generation) && _offerHandle == handle;

    private void PublishCurrent(string id, long generation, AutoSaveState state, string? message)
    {
        DateTimeOffset? saved;
        lock (_sync)
        {
            if (!IsCurrentLocked(id, generation)) return;
            saved = _lastSavedAt;
        }
        _host.Publish(new AutoSaveStatus(id, state, saved, message, generation));
    }

    private void Publish(AutoSaveState state, string? message)
    {
        string? id;
        DateTimeOffset? saved;
        long generation;
        lock (_sync)
        {
            id = _activeId;
            saved = _lastSavedAt;
            generation = _generation;
        }
        if (id is not null) _host.Publish(new AutoSaveStatus(id, state, saved, message, generation));
    }

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await _clock.DelayAsync(PollInterval, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
            try { await TickOnceAsync(ct).ConfigureAwait(false); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception) { /* a desktop read failed; keep watching and retry next poll */ }
        }
    }
}
