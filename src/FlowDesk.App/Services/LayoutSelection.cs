using FlowDesk.Core.Capture;
using FlowDesk.Core.Model;
using FlowDesk.Core.Windows;

namespace FlowDesk.App.Services;

public static class LayoutSelection
{
    public static bool Includes(WindowInfo window, string? monitor, int ownProcessId) =>
        window.ProcessId != ownProcessId && window.Show != WindowShow.Minimized
        && DesktopCaptureFilter.IsLocalForLayout(window)
        && string.Equals(window.MonitorDevice, monitor, StringComparison.OrdinalIgnoreCase);
}
