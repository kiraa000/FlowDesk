namespace FlowDesk.Core.QuickLook;

/// <summary>UI-thread-owned explicit-path preview lease; it never borrows Explorer's selection.</summary>
public sealed class QuickLookPathSession
{
    public string? Path { get; private set; }
    public nint Owner { get; private set; }
    public bool Active => Path is not null;
    public bool Begin(string path, nint owner, nint foreground, bool enabled)
    {
        if (!enabled || owner == 0 || owner != foreground || !System.IO.Path.IsPathFullyQualified(path)) return false;
        Path = path; Owner = owner; return true;
    }
    public bool BelongsTo(nint owner) => Active && owner != 0 && Owner == owner;
    public void Clear() { Path = null; Owner = 0; }
}
