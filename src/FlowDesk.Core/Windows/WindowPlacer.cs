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
        {
            api.SetRect(target, asynchronous: true);
            return PlaceOutcome.Ok;
        }

        bool min = api.IsMinimized, max = api.IsMaximized;
        if (show == WindowShow.Minimized && min) return PlaceOutcome.Ok;
        if (show == WindowShow.Maximized && !min && max && api.IsOnTargetMonitor(target)) return PlaceOutcome.Ok;

        // SW_SHOWNOACTIVATE normally restores to normal without stealing focus. Some hosted windows
        // can ignore the request; use the explicit restore only if the window remains iconic.
        if (min || max) api.Show(SW_SHOWNOACTIVATE);
        if (api.IsMinimized) api.Show(SW_RESTORE);
        if (api.IsMaximized) api.Show(SW_SHOWNORMAL);
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
}
