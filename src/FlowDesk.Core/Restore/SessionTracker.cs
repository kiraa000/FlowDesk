using System.Collections.Concurrent;
using FlowDesk.Core.Model;
using FlowDesk.Core.Windows;

namespace FlowDesk.Core.Restore;

/// <summary>
/// Runtime-only memory of which live window belongs to which workspace item, plus
/// the "session" snapshot taken when the user leaves a workspace (so A -> B -> A
/// returns A exactly as it was left, not as its saved layout).
/// Window handles are never persisted: they are meaningless after a reboot.
/// </summary>
public sealed class SessionTracker
{
    private sealed record Bound(long Handle, string AppKey, int Pid);

    private readonly ConcurrentDictionary<(string Ws, string Item), Bound> _bound = new();
    private readonly ConcurrentDictionary<string, Dictionary<string, SavedWindow>> _sessions = new();

    public void Bind(string workspaceId, string itemId, WindowInfo w) =>
        _bound[(workspaceId, itemId)] = new Bound(w.Handle, w.AppKey, w.ProcessId);

    public void Unbind(string workspaceId, string itemId) => _bound.TryRemove((workspaceId, itemId), out _);

    /// <summary>The live window still bound to this item, or null if it closed (or the handle was reused).</summary>
    public WindowInfo? Live(IWindowSystem ws, string workspaceId, string itemId)
    {
        if (!_bound.TryGetValue((workspaceId, itemId), out var b)) return null;
        var w = ws.IsAlive(b.Handle) ? ws.Get(b.Handle) : null;
        if (w is null || w.ProcessId != b.Pid || w.AppKey != b.AppKey)
        {
            _bound.TryRemove((workspaceId, itemId), out _);
            return null;
        }
        return w;
    }

    /// <summary>All live windows bound to a workspace, keyed by item id.</summary>
    public Dictionary<string, WindowInfo> LiveWindows(IWindowSystem ws, Workspace workspace)
    {
        var result = new Dictionary<string, WindowInfo>();
        foreach (var item in workspace.Items)
            if (Live(ws, workspace.Id, item.Id) is { } w) result[item.Id] = w;
        return result;
    }

    public IEnumerable<long> HandlesOf(string workspaceId) =>
        _bound.Where(kv => kv.Key.Ws == workspaceId).Select(kv => kv.Value.Handle);

    public void SetSession(string workspaceId, Dictionary<string, SavedWindow> placements) => _sessions[workspaceId] = placements;

    public Dictionary<string, SavedWindow>? GetSession(string workspaceId) =>
        _sessions.TryGetValue(workspaceId, out var s) ? s : null;

    public void ClearSession(string workspaceId) => _sessions.TryRemove(workspaceId, out _);
}
