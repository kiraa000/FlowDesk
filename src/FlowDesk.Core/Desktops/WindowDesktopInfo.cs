namespace FlowDesk.Core.Desktops;

/// <summary>Read-only identity of a window's desktop. Unknown is never treated as Desktop 1.</summary>
public readonly record struct WindowDesktopInfo(int? Index, Guid? Id, bool? IsCurrent)
{
    public bool HasIdentity => Index is >= 0 || (Id is { } id && id != Guid.Empty);
    /// <summary>Only positive, comparable identities can rule out a detached UWP candidate.</summary>
    public bool ConflictsWith(WindowDesktopInfo other)
    {
        if (Id is { } id && id != Guid.Empty && other.Id is { } otherId && otherId != Guid.Empty)
            return id != otherId; // a matching GUID wins while numeric ordering is changing
        return Index is >= 0 && other.Index is >= 0 && Index != other.Index;
    }

    public static WindowDesktopInfo Read(IVirtualDesktops desktops, long handle)
    {
        int? index = desktops.GetWindowDesktopNumber(handle);
        if (index < 0) index = null;
        Guid? id = desktops.GetWindowDesktopId(handle);
        if (id == Guid.Empty) id = null;
        bool? current = desktops.IsWindowOnCurrent(handle);
        if (current is null && index is { } number && desktops.GetCurrent() is { } active) current = number == active;
        return new(index, id, current);
    }
}
