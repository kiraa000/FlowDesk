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
    /// <summary>True when the backend loaded and exposes every function FlowDesk needs.</summary>
    bool IsAvailable { get; }

    /// <summary>One plain sentence for Settings, e.g. why desktop shortcuts are unavailable.</summary>
    string Status { get; }

    int? GetCount();
    int? GetCurrent();
    bool GoTo(int index);
    bool MoveWindow(long handle, int index);
    bool? IsWindowOnCurrent(long handle);

    /// <summary>Adds one desktop without switching to it. Returns the new count, or null on failure.</summary>
    int? Create();
}
