using FlowDesk.Core.Capture;
using FlowDesk.Core.Model;
using FlowDesk.Core.Windows;

namespace FlowDesk.Core.Restore;

/// <summary>What happens to the old workspace's windows when switching away.</summary>
public enum LeaveAction { Minimize, Leave }

/// <summary>
/// A -> B -> A: leaving A remembers exactly how its windows were (session snapshot) and tucks them
/// away; opening A again restores that snapshot instead of A's saved layout. Windows shared by A and B
/// (same app item in both) are simply moved, never minimized.
/// </summary>
public sealed class WorkspaceSwitcher(IWindowSystem windows, SessionTracker tracker, WorkspaceCapture capture, WorkspaceRestorer restorer)
{
    public string? ActiveWorkspaceId { get; private set; }

    public async Task<RestoreReport> SwitchToAsync(Workspace target, Workspace? current, LeaveAction leave = LeaveAction.Minimize,
        IProgress<ItemResult>? progress = null, CancellationToken ct = default)
    {
        HashSet<long> currentHandles = [];
        if (current is not null && current.Id != target.Id)
        {
            tracker.SetSession(current.Id, capture.SnapshotSession(current));
            currentHandles = tracker.LiveWindows(windows, current).Values.Select(w => w.Handle).ToHashSet();
        }

        var session = tracker.GetSession(target.Id);
        var report = await restorer.RestoreAsync(target, new RestoreOptions { Overrides = session }, progress, ct).ConfigureAwait(false);

        if (leave == LeaveAction.Minimize && currentHandles.Count > 0)
        {
            var targetHandles = tracker.HandlesOf(target.Id).ToHashSet();
            foreach (var h in currentHandles.Where(h => !targetHandles.Contains(h)))
                windows.Minimize(h);
        }

        target.LastUsed = DateTimeOffset.Now;
        ActiveWorkspaceId = target.Id;
        return report;
    }

    /// <summary>Forget the session snapshot so the next open uses the saved layout ("Reset to saved layout").</summary>
    public void ResetSession(string workspaceId) => tracker.ClearSession(workspaceId);
}
