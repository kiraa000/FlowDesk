using FlowDesk.Core.Updates;

namespace FlowDesk.App.Services;

public sealed record UpdateState(UpdateRelease? Pending, bool Busy, double? Progress, string? Error);

/// <summary>UI-thread-owned lifecycle. No timer callbacks overlap, and every late result is gated.</summary>
public sealed class UpdateCoordinator : IDisposable
{
    private readonly UpdateTransport transport;
    private readonly Func<AppSettings> settings;
    private readonly Action persist;
    private readonly Action<Exception> log;
    private readonly Func<VerifiedInstaller, Task<bool>> handoff;
    private readonly Func<CancellationToken, Task> prune;
    private readonly UpdateVersion? current;
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? operation;
    private Task? loop;
    private bool stopped, checking, pruning, startupPruneAttempted;
    public UpdateState State { get; private set; } = new(null, false, null, null);
    public event Action? StateChanged;
    public bool CanInstall => !stopped && !pruning && settings().CheckForUpdates && State.Pending is not null && !State.Busy;
    public bool HandoffAllowed => !stopped && settings().CheckForUpdates && State.Busy && operation is { IsCancellationRequested: false };
    public Uri? ReleaseNotes => State.Pending?.ReleaseNotes ?? new Uri(AppLinks.Releases);

    public UpdateCoordinator(UpdateTransport transport, string runningVersion, Func<AppSettings> settings,
        Action persist, Action<Exception> log, Func<VerifiedInstaller, Task<bool>> handoff,
        Func<CancellationToken, Task>? prune = null)
    {
        this.transport = transport; this.settings = settings; this.persist = persist; this.log = log; this.handoff = handoff;
        this.prune = prune ?? (token => Task.Run(() => UpdateTransport.PruneDownloads(Path.GetTempPath(), log, token), token));
        UpdateVersion.TryParse(runningVersion, out current);
    }

    public void Start() { if (!stopped) loop ??= RunAsync(); }
    private async Task RunAsync()
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(30), lifetime.Token);
            await PruneAtStartupAsync();
            while (!stopped)
            {
                await CheckIfDueAsync(DateTimeOffset.UtcNow);
                // Minute granularity accommodates opt-in/import and clock changes without network polling.
                await Task.Delay(TimeSpan.FromMinutes(1), lifetime.Token);
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex) { log(ex); }
    }

    public async Task PruneAtStartupAsync()
    {
        if (stopped || startupPruneAttempted) return;
        startupPruneAttempted = true;
        // UI-owned exclusion is set before the first await. If an install already owns the tree,
        // leave it entirely alone this run. Conversely InstallAsync cannot begin while pruning.
        if (State.Busy) return;
        pruning = true;
        try
        {
            if (State.Pending is not null) SetState(State); // refresh CanInstall on notice/tray
            await prune(lifetime.Token);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex) { log(ex); }
        finally
        {
            pruning = false;
            if (!stopped && State.Pending is not null) SetState(State);
        }
    }

    public async Task CheckIfDueAsync(DateTimeOffset now)
    {
        if (stopped || checking || State.Busy || current is null || !settings().CheckForUpdates) return;
        var last = settings().LastUpdateCheck;
        if (last.HasValue && now - last.Value < TimeSpan.FromHours(24) && last <= now + TimeSpan.FromHours(24)) return;
        checking = true;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        operation = cancellation;
        try
        {
            // Persist attempted checks too: offline/rate-limit errors retry at the next daily slot.
            settings().LastUpdateCheck = now;
            persist();
            if (!settings().CheckForUpdates || stopped) return;
            var release = await transport.CheckAsync(cancellation.Token);
            if (release is null && !cancellation.IsCancellationRequested)
                log(new InvalidDataException("No usable update release"));
            if (!stopped && !cancellation.IsCancellationRequested && settings().CheckForUpdates && release is not null &&
                UpdateRelease.ShouldOffer(release, current, settings().SkippedUpdateVersion))
                SetState(new(release, false, null, null));
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex) { log(ex); } // Check errors never surface UI or include URL/user data.
        finally { if (operation == cancellation) operation = null; checking = false; }
    }

    public void PreferenceChanged()
    {
        if (!settings().CheckForUpdates)
        {
            operation?.Cancel();
            SetState(new(null, State.Busy, null, null));
        }
    }
    public void Skip()
    {
        if (State.Pending is null || State.Busy || stopped) return;
        settings().SkippedUpdateVersion = State.Pending.Version.ToString();
        persist();
        SetState(new(null, false, null, null));
    }
    public void CancelDownload() { if (State.Busy) operation?.Cancel(); }
    public async Task InstallAsync()
    {
        if (!CanInstall || checking) return;
        var release = State.Pending!;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        operation = cancellation;
        SetState(new(release, true, null, null));
        try
        {
            var progress = new Progress<double?>(value =>
            {
                if (!stopped && operation == cancellation && !cancellation.IsCancellationRequested)
                    SetState(State with { Progress = value });
            });
            using var installer = await transport.PrepareAsync(release, Path.GetTempPath(), progress, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (!settings().CheckForUpdates || stopped) return;
            if (!await handoff(installer) && !stopped && !cancellation.IsCancellationRequested && settings().CheckForUpdates)
                SetState(new(release, false, null, "FlowDesk could not start the update. Try again or download it from the releases page."));
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (UpdateVerificationException ex)
        { if (!stopped && !cancellation.IsCancellationRequested && settings().CheckForUpdates) SetState(new(release, false, null, ex.Message)); }
        catch (Exception ex)
        {
            log(ex);
            if (!stopped && !cancellation.IsCancellationRequested && settings().CheckForUpdates)
                SetState(new(release, false, null, "FlowDesk could not download or start the update. Try again or download it from the releases page."));
        }
        finally
        {
            if (operation == cancellation) operation = null;
            if (!stopped && State.Busy) SetState(State with { Busy = false, Progress = null });
        }
    }
    private void SetState(UpdateState state) { State = state; if (!stopped) StateChanged?.Invoke(); }
    public void Stop()
    {
        if (stopped) return;
        stopped = true;
        lifetime.Cancel();
        operation?.Cancel();
    }
    public void Dispose()
    {
        Stop();
        // Don't dispose a client under a response read. Cancel first, then release after its owner unwinds.
        // HttpClient.Dispose is safe with outstanding canceled requests and does not block the UI thread.
        transport.Dispose();
    }
}
