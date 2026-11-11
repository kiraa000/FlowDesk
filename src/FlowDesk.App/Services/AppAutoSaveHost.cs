using FlowDesk.App.Autosave;
using FlowDesk.Core.Capture;
using FlowDesk.Core.Model;
using FlowDesk.Core.Windows;

namespace FlowDesk.App.Services;

/// <summary>
/// Bridges the autosave coordinator to the core: all window enumeration runs on a worker thread; only small
/// model snapshots and applies touch the UI thread. A UI commit is rejected unless its activation generation is
/// still current and no core switch/manual operation is busy. Every save is written before a saved state.
/// </summary>
public sealed class AppAutoSaveHost : IAutoSaveHost
{
    private readonly AppServices _services;
    private readonly IUserInteraction _ui;
    private readonly Func<string, Workspace?> _workspace;
    private readonly Func<string, bool> _isActive;
    private readonly Func<bool> _isBusy;
    private readonly Action<AutoSaveStatus> _publish;
    private readonly Action<NewWindowOffer> _offer;
    private readonly Action<long> _dismissOffer;
    private readonly Action<string> _problem;
    private readonly Action _positionsApplied;
    private readonly Action _itemsChanged;
    private readonly Dictionary<string, string> _names = new(StringComparer.OrdinalIgnoreCase);

    public AppAutoSaveHost(AppServices services, IUserInteraction ui,
        Func<string, Workspace?> workspace, Func<string, bool> isActive, Func<bool> isBusy,
        Action<AutoSaveStatus> publish, Action<NewWindowOffer> offer, Action<long> dismissOffer,
        Action<string> problem, Action positionsApplied, Action itemsChanged)
    {
        _services = services; _ui = ui;
        _workspace = workspace; _isActive = isActive; _isBusy = isBusy;
        _publish = publish; _offer = offer; _dismissOffer = dismissOffer;
        _problem = problem; _positionsApplied = positionsApplied; _itemsChanged = itemsChanged;
    }

    public async Task<WorkspaceObservation?> ObserveAsync(string workspaceId, CancellationToken ct)
    {
        var workspace = await OnUiAsync(() => _workspace(workspaceId)?.Clone()).ConfigureAwait(false);
        if (workspace is null) return null;

        return await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            var bound = _services.Tracker.LiveWindows(_services.Windows, workspace);
            var boundHandles = bound.Values.Select(w => w.Handle).ToHashSet();
            var itemByHandle = bound.GroupBy(kv => kv.Value.Handle).ToDictionary(g => g.Key, g => g.First().Key);

            var open = _services.Windows.GetAppWindows()
                .Where(w => w.ProcessId != _services.Windows.OwnProcessId
                    && !w.Unresolved
                    && (!string.IsNullOrEmpty(w.ExePath) || !string.IsNullOrEmpty(w.Aumid)))
                .ToList();
            var matches = open.ToDictionary(w => w.Handle, w => NewWindowEligibility.MatchesMissingLiveItem(w, workspace, bound));
            var windows = open.Select(w => new ObservedWindow(
                    w.Handle, SafeName(w), IsOwnProcess: false,
                    IsBoundToItem: boundHandles.Contains(w.Handle),
                    MatchesMissingLiveItem: matches[w.Handle]))
                .ToList();

            // Bound handles catch a reopened/replacement window even at the same rectangle. Handles that match a
            // missing item are included too, so a replacement appearing later still triggers SaveLayout to bind it.
            var signature = string.Join('\n', open
                .Where(w => boundHandles.Contains(w.Handle) || matches[w.Handle])
                .Select(w => $"{w.Handle}:{(itemByHandle.TryGetValue(w.Handle, out var itemId) ? itemId : "replacement")}:" +
                    $"{w.NormalRect.X},{w.NormalRect.Y},{w.NormalRect.Width},{w.NormalRect.Height}:{w.Show}:{w.MonitorDevice}")
                .OrderBy(s => s, StringComparer.Ordinal));
            return new WorkspaceObservation(workspaceId, signature, windows);
        }, ct).ConfigureAwait(false);
    }

    public async Task<bool?> SaveLayoutAsync(string workspaceId, long generation, Func<bool> isCurrent,
        CancellationToken ct)
    {
        if (!isCurrent()) return null;
        var clone = await OnUiAsync(() => _workspace(workspaceId)?.Clone()).ConfigureAwait(false);
        if (clone is null) return null;

        // Even zero updated positions must persist the current model: a just-added item may have no live window,
        // and a retry after a failed Add/persist must be able to write the retained item.
        await Task.Run(() => _services.Capture.SaveLayout(clone), ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();

        return await OnUiAsync(async () =>
        {
            if (!isCurrent() || !_isActive(workspaceId) || _isBusy()) return (bool?)null;
            ct.ThrowIfCancellationRequested();
            var current = _workspace(workspaceId);
            if (current is null) return null;
            foreach (var saved in clone.Items)
            {
                var item = current.Items.FirstOrDefault(i => i.Id == saved.Id);
                if (item is not null) item.Window = saved.Window?.Clone();
            }
            DateTimeOffset? previousSavedAt = current.LayoutSavedAt;
            current.LayoutSavedAt = clone.LayoutSavedAt;
            _positionsApplied();
            bool persisted = await _services.SaveNowAsync();
            if (!persisted)
            {
                // Do not let an unpersisted timestamp become the "last saved" shown in the UI.
                current.LayoutSavedAt = previousSavedAt;
                _positionsApplied();
            }
            return persisted;
        }).ConfigureAwait(false);
    }

    public async Task<AddWindowOutcome> AddWindowAsync(string workspaceId, long generation, long handle,
        Func<bool> isCurrent, CancellationToken ct)
    {
        if (!isCurrent()) return AddWindowOutcome.NotEligible;
        var clone = await OnUiAsync(() => _workspace(workspaceId)?.Clone()).ConfigureAwait(false);
        if (clone is null) return AddWindowOutcome.NotEligible;

        return await Task.Run(async () =>
        {
            ct.ThrowIfCancellationRequested();
            if (!isCurrent()) return AddWindowOutcome.NotEligible;
            // Live re-check off the UI: closed, already bound, or now the intended replacement for any missing item.
            var window = _services.Windows.Get(handle);
            if (window is null) return AddWindowOutcome.NotEligible;
            var bound = _services.Tracker.LiveWindows(_services.Windows, clone);
            if (bound.Values.Any(w => w.Handle == handle)) return AddWindowOutcome.NotEligible;
            if (NewWindowEligibility.MatchesMissingLiveItem(window, clone, bound)) return AddWindowOutcome.NotEligible;

            var candidate = _services.Capture.CaptureOpenWindows().FirstOrDefault(c => c.Window.Handle == handle);
            if (candidate is null)
            {
                Problem("That window is no longer available.");
                return AddWindowOutcome.NotEligible;
            }
            if (candidate.Problem is { } problem)
            {
                Problem($"That window could not be added: {problem}");
                return AddWindowOutcome.NotEligible;
            }
            var item = candidate.Item.Clone();

            return await OnUiAsync(async () =>
            {
                if (!isCurrent() || !_isActive(workspaceId) || _isBusy()) return AddWindowOutcome.NotEligible;
                ct.ThrowIfCancellationRequested();
                var workspace = _workspace(workspaceId);
                if (workspace is null) return AddWindowOutcome.NotEligible;
                workspace.Items.Add(item);
                _services.Tracker.Bind(workspace.Id, item.Id, candidate.Window);
                _itemsChanged();
                bool saved = await _services.SaveNowAsync();
                return saved ? AddWindowOutcome.Added : AddWindowOutcome.PersistenceFailed;
            }).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    public void Publish(AutoSaveStatus status) => _ui.Enqueue(() => _publish(status));
    public void Offer(NewWindowOffer offer) => _ui.Enqueue(() => _offer(offer));
    public void DismissOffer(long generation) => _ui.Enqueue(() => _dismissOffer(generation));

    private void Problem(string message) => _ui.Enqueue(() => _problem(message));

    private string SafeName(WindowInfo w)
    {
        string key = w.ExePath ?? "";
        if (key.Length > 0 && _names.TryGetValue(key, out var cached)) return cached;
        string name;
        try
        {
            name = WorkspaceCapture.DisplayName(w);
            if (string.IsNullOrWhiteSpace(name)) name = w.Title;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            name = string.IsNullOrWhiteSpace(w.Title) ? w.ExeName : w.Title;
        }
        if (key.Length > 0) _names[key] = name;
        return name;
    }

    private Task<T> OnUiAsync<T>(Func<T> func)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ui.Enqueue(() =>
        {
            try { tcs.TrySetResult(func()); }
            catch (Exception ex) { tcs.TrySetException(ex); }
        });
        return tcs.Task;
    }

    private Task<T> OnUiAsync<T>(Func<Task<T>> func)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ui.Enqueue(async () =>
        {
            try { tcs.TrySetResult(await func()); }
            catch (Exception ex) { tcs.TrySetException(ex); }
        });
        return tcs.Task;
    }
}
