using FlowDesk.Core.Model;
using FlowDesk.Core.Windows;

namespace FlowDesk.App.ViewModels;

/// <summary>
/// Compact user-facing virtual desktop labels for the save picker and the running-app picker.
/// Desktop numbers are stored 0-based and shown 1-based, matching "Desktop 1" in Windows and the
/// Alt+1..5 shortcuts. When only "on another desktop" is known, say so instead of inventing a number.
/// </summary>
internal static class DesktopLabels
{
    /// <summary>
    /// "Desktop 2 (current)", "Other desktop", or "" when no backend could tell. A window on the
    /// current desktop whose own number is unknown still gets the number when the caller knows the
    /// current desktop (see <see cref="CurrentDesktopIndex"/>); "Current desktop" is only the last
    /// resort when no number is known at all.
    /// </summary>
    public static string For(WindowInfo window, int? currentDesktopIndex = null)
    {
        if (window.DesktopIndex is int index)
        {
            string label = $"Desktop {index + 1}";
            return window.IsOnCurrentDesktop == true ? label + " (current)" : label;
        }
        return window.IsOnCurrentDesktop switch
        {
            true => currentDesktopIndex is int current ? $"Desktop {current + 1} (current)" : "Current desktop",
            false => "Other desktop",
            _ => "",
        };
    }

    /// <summary>
    /// The current desktop number as told by any window on it. Windows on the current desktop can
    /// still lack their own index (pinned/frames), so the picker passes this number down as context.
    /// </summary>
    public static int? CurrentDesktopIndex(IEnumerable<WindowInfo> windows)
    {
        foreach (var window in windows)
            if (window.IsOnCurrentDesktop == true && window.DesktopIndex is int index) return index;
        return null;
    }

    /// <summary>Label for a saved window; it keeps no "current" flag, only the desktop it was on.</summary>
    public static string For(SavedWindow? window) =>
        window?.DesktopIndex is int index ? $"Desktop {index + 1}"
        : window?.DesktopId is not null ? "Desktop (number unavailable)" : "";

    /// <summary>Prefixes a secondary line with the desktop, unless the backend could not tell.</summary>
    public static string Prefix(WindowInfo window, string text, int? currentDesktopIndex = null)
    {
        string desktop = For(window, currentDesktopIndex);
        return desktop.Length == 0 ? text : $"{desktop} · {text}";
    }

    /// <summary>
    /// Current desktop first, then desktop number (unknown last). OrderBy is stable, so capture order
    /// is kept inside one desktop and no two entries jump around between refreshes.
    /// </summary>
    public static (int Group, int Index) SortKey(WindowInfo window) =>
        (window.IsOnCurrentDesktop == true ? 0 : 1, window.DesktopIndex ?? int.MaxValue);
}
