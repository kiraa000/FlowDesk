using FlowDesk.Core.Model;
using static FlowDesk.Core.Interop.Native;

namespace FlowDesk.Core.Windows;

// Narrow native boundary so minimized/maximized transitions can be tested without creating real windows.
internal interface IWindowPlacementApi
{
    bool IsWindow { get; }
    bool IsHung { get; }
    bool IsMinimized { get; }
    bool IsMaximized { get; }
    bool IsOnTargetMonitor(PixelRect target);
    uint Dpi { get; }
    bool SetRect(PixelRect target, bool asynchronous = false);
    bool SetPlacementAsync(PixelRect target, WindowShow show);
    bool RectMatches(PixelRect target);
    int LastError { get; }
    void Show(int command);
    void Pause();
}

internal static class WindowPlacer
{
    internal static PlaceOutcome Place(IWindowPlacementApi api, PixelRect target, WindowShow show)
    {
        if (!api.IsWindow) return PlaceOutcome.WindowGone;
        if (api.IsHung)
            return PlaceAsynchronously(api, target, show);

        bool min = api.IsMinimized, max = api.IsMaximized;
        if (show == WindowShow.Minimized && min) return PlaceOutcome.Ok;
        if (show == WindowShow.Maximized && !min && max && api.IsOnTargetMonitor(target)) return PlaceOutcome.Ok;

        // SW_SHOWNOACTIVATE normally restores to normal without stealing focus. Some hosted windows
        // can ignore the request; use the explicit restore only if the window remains iconic.
        if (min || max) api.Show(SW_SHOWNOACTIVATE);
        if (api.IsMinimized) api.Show(SW_RESTORE);
        if (api.IsMaximized) api.Show(SW_SHOWNORMAL);
        // Hosted/suspended frames may not apply a synchronous show request immediately. Do not
        // report success after merely changing the restore rect while the frame is still iconic.
        if (show != WindowShow.Minimized && api.IsMinimized)
            return PlaceAsynchronously(api, target, show);
        uint dpiBefore = api.Dpi;
        if (!api.SetRect(target))
            return api.LastError == 5 ? PlaceOutcome.AccessDenied : PlaceOutcome.Failed;
        if (api.Dpi != dpiBefore || !api.RectMatches(target))
        {
            api.Pause();
            api.SetRect(target);
        }
        switch (show)
        {
            case WindowShow.Maximized: api.Show(SW_SHOWMAXIMIZED); break;
            case WindowShow.Minimized: api.Show(SW_SHOWMINNOACTIVE); break;
        }
        return PlaceOutcome.Ok;
    }

    private static PlaceOutcome PlaceAsynchronously(IWindowPlacementApi api, PixelRect target, WindowShow show)
    {
        // WPF_ASYNCWINDOWPLACEMENT carries BOTH the restore rectangle and the show command to the
        // owning thread. SWP_ASYNCWINDOWPOS alone never unminimizes an ApplicationFrameWindow.
        if (!api.SetPlacementAsync(target, show))
            return api.LastError == 5 ? PlaceOutcome.AccessDenied : PlaceOutcome.Failed;
        for (int poll = 0; poll <= 20; poll++)
        {
            if (!api.IsWindow) return PlaceOutcome.WindowGone;
            bool stateMatches = show switch
            {
                WindowShow.Minimized => api.IsMinimized,
                WindowShow.Maximized => !api.IsMinimized && api.IsMaximized && api.IsOnTargetMonitor(target),
                _ => !api.IsMinimized && !api.IsMaximized && api.IsOnTargetMonitor(target),
            };
            if (stateMatches) return PlaceOutcome.Ok;
            if (poll < 20) api.Pause(); // bounded 600 ms; never call synchronous ShowWindow on a hung frame
        }
        return PlaceOutcome.Failed;
    }

    internal static PixelRect ToWorkspaceRect(PixelRect screen, PixelRect monitor, PixelRect workArea, bool toolWindow) =>
        toolWindow ? screen : screen with { X = screen.X - (workArea.X - monitor.X), Y = screen.Y - (workArea.Y - monitor.Y) };

    // A queued rectangle update must not activate a window already in the requested state.
    // Explicit restoring/maximizing is reserved for a real state/monitor transition.
    internal static int AsyncShowCommand(WindowShow requested, bool minimized, bool maximized, bool onTargetMonitor) => requested switch
    {
        WindowShow.Minimized => SW_SHOWMINNOACTIVE,
        WindowShow.Maximized when !minimized && maximized && onTargetMonitor => SW_SHOWNA,
        WindowShow.Maximized => SW_SHOWMAXIMIZED,
        _ when !minimized && !maximized => SW_SHOWNOACTIVATE,
        _ => SW_RESTORE,
    };
}
