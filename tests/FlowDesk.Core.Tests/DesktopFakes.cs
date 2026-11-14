using FlowDesk.Core.Desktops;

namespace FlowDesk.Core.Tests;

/// <summary>
/// Implements only the original members: the newer desktop-lookup methods must have default (null)
/// implementations so older backends and third-party implementations still compile and run.
/// </summary>
internal sealed class MinimalVirtualDesktops : IVirtualDesktops
{
    public bool IsAvailable => true;
    public string Status => "Minimal backend.";
    public int? GetCount() => 2;
    public int? GetCurrent() => 0;
    public bool GoTo(int index) => true;
    public bool MoveWindow(long handle, int index) => true;
    public bool? IsWindowOnCurrent(long handle) => null;
    public int? Create() => 2;
    public void Dispose() { }
}
