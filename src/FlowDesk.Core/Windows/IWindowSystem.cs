using FlowDesk.Core.Model;

namespace FlowDesk.Core.Windows;

/// <summary>A top-level window as FlowDesk sees it. Handles are stored as long so tests can fake them.</summary>
public sealed record WindowInfo
{
    public required long Handle { get; init; }
    /// <summary>Process that owns the content (for UWP frames: the app inside the frame, not ApplicationFrameHost).</summary>
    public required int ProcessId { get; init; }
    public required string ExePath { get; init; }
    public string? Aumid { get; init; }
    /// <summary>Arguments the process was started with (null if unreadable).</summary>
    public string? Arguments { get; init; }
    public required string Title { get; init; }
    public required string ClassName { get; init; }
    /// <summary>Outer rect of the window in its normal (restored) state.</summary>
    public required PixelRect NormalRect { get; init; }
    public required WindowShow Show { get; init; }
    public string MonitorDevice { get; init; } = "";
    /// <summary>True for a UWP frame whose app could not be identified (app suspended).</summary>
    public bool Unresolved { get; init; }

    public string AppKey => AppKeys.For(ExePath, Aumid);
    public string ExeName => AppKeys.ExeName(ExePath);
}

public sealed record MonitorInfo(string Device, PixelRect Bounds, PixelRect WorkArea, bool IsPrimary, int Dpi);

public enum PlaceOutcome { Ok, WindowGone, AccessDenied, Failed }

/// <summary>
/// Everything the restorer, capture and focus watcher need from the OS. The real
/// implementation is <see cref="Win32WindowSystem"/>; tests use a fake.
/// </summary>
public interface IWindowSystem
{
    /// <summary>Alt-Tab-style app windows on the current virtual desktop, front to back.</summary>
    IReadOnlyList<WindowInfo> GetAppWindows();
    WindowInfo? Get(long handle);
    bool IsAlive(long handle);
    IReadOnlyList<MonitorInfo> GetMonitors();
    /// <summary>Moves/resizes the window to <paramref name="normalRect"/> (outer rect) and applies the show state.</summary>
    PlaceOutcome Place(long handle, PixelRect normalRect, WindowShow show);
    /// <summary>
    /// Difference between the outer rect and the visible frame (invisible resize borders on Windows 10/11),
    /// as (left, top, right, bottom) amounts to ADD to a visible rect to get the outer rect.
    /// </summary>
    (int L, int T, int R, int B) GetInvisibleBorders(long handle);
    void BringToFront(long handle);
    bool Activate(long handle);
    void Close(long handle);
    void Minimize(long handle);
    int OwnProcessId { get; }
}
