using FlowDesk.Core.Model;

namespace FlowDesk.App.Services;

/// <summary>A short-lived data-only undo. It never reclaims live windows or silently reactivates a deleted workspace.</summary>
internal sealed class WorkspaceDeleteUndo
{
    internal sealed record Snapshot(Workspace Workspace, int Index, DateTimeOffset ExpiresAt);
    private readonly object gate = new();
    private Snapshot? current;
    public bool HasValue { get { lock (gate) return current is not null; } }
    public Snapshot Arm(Workspace workspace, int index, DateTimeOffset now)
    {
        lock (gate) return current = new(workspace.Clone(), index, now.AddSeconds(10));
    }
    public Snapshot? Take(DateTimeOffset now)
    {
        lock (gate)
        {
            var value = current; current = null;
            return value is not null && now < value.ExpiresAt ? value : null;
        }
    }
    public void Invalidate() { lock (gate) current = null; }
    public bool Expire(Snapshot expected)
    {
        lock (gate)
        {
            if (!ReferenceEquals(current, expected)) return false;
            current = null; return true;
        }
    }
}
