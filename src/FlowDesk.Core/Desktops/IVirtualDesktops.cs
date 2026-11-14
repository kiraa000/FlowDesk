namespace FlowDesk.Core.Desktops;

/// <summary>
/// Virtual desktop operations. Windows has no public API for switching desktops, so the real
/// implementation (<see cref="VirtualDesktopAccessor"/>) wraps the third-party
/// VirtualDesktopAccessor.dll, which tracks the undocumented COM interfaces per Windows build.
/// Desktop numbers are 0-based. Every method returns null/false instead of throwing when the
/// backend is missing, fails or does not answer in time.
/// </summary>
public interface IVirtualDesktops : IDisposable
{
    /// <summary>True when native desktop operations loaded; optional creation/lookup may still be unavailable.</summary>
    bool IsAvailable { get; }

    /// <summary>One plain sentence for Settings, e.g. why desktop shortcuts are unavailable.</summary>
    string Status { get; }

    int? GetCount();
    int? GetCurrent();
    bool GoTo(int index);
    bool MoveWindow(long handle, int index);
    bool? IsWindowOnCurrent(long handle);

    /// <summary>0-based desktop number; null for pinned/unmapped windows or an older DLL.</summary>
    int? GetWindowDesktopNumber(long handle) => null;
    /// <summary>Public Windows desktop identity; null when unknown. Does not imply movement support.</summary>
    Guid? GetWindowDesktopId(long handle) => null;

    /// <summary>Adds one desktop without switching to it. Returns the new count, or null on failure.</summary>
    int? Create();
}
