using FlowDesk.Core.Model;
using FlowDesk.Core.Restore;

namespace FlowDesk.App.Services;

/// <summary>One browser family's own "restore my last session" setting. FlowDesk never changes it.</summary>
public sealed record BrowserRestoreNotice(string Key, string BrowserName, string Instructions);

public static class BrowserRestoreHelp
{
    /// <summary>
    /// Browser App item that uses the browser's own session restore. Deliberately does not look at
    /// <see cref="WorkspaceItem.ReopenLastTabs"/>: the toggle must be detectable while it is off.
    /// </summary>
    public static bool IsBrowserItem(WorkspaceItem item) =>
        item.Kind == ItemKind.App && item.App is { } app
        && string.IsNullOrEmpty(app.Aumid) && ShellLauncher.IsBrowser(app.ExePath);

    public static BrowserRestoreNotice For(string? exePath, string? fallbackName)
    {
        string exeName = AppKeys.ExeName(exePath);
        switch (exeName)
        {
            case "msedge":
                return new("msedge", "Edge",
                    "Edge: Settings > Start, home, and new tabs > Open tabs from the previous session.");
            case "chrome":
                return new("chrome", "Chrome",
                    "Chrome: Settings > On startup > Continue where you left off.");
            case "firefox":
                return new("firefox", "Firefox",
                    "Firefox: Settings > General > Open previous windows and tabs.");
        }

        string name = string.IsNullOrWhiteSpace(fallbackName) ? exeName : fallbackName;
        bool gecko = BrowserSession.IsGecko(exePath);
        string instructions = gecko
            ? $"{name}: open this browser's General or Startup settings and turn on restoring the previous session."
            : $"{name}: open this browser's startup settings and choose the option that reopens the pages from your last session.";
        return new("exe:" + exeName, name, instructions);
    }

    public const string Caveat =
        "FlowDesk starts the browser with its profile and no links, so the browser must restore its own tabs. " +
        "It only restores when the browser is not already running; if it is, FlowDesk reuses its open windows. " +
        "Several windows may come back: FlowDesk matches them by title and leaves extras where the browser puts them. " +
        "FlowDesk cannot read tabs or change browser settings.";
}
