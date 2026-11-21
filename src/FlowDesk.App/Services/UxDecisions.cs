namespace FlowDesk.App.Services;

public enum CloseAction { Exit, Hide }

/// <summary>Pure decisions shared by the live UI and headless regression tests.</summary>
public static class UxDecisions
{
    public const bool InitialWindowSelection = false;
    public static string NextWorkspaceName(IEnumerable<string> names)
    {
        var used = names.Select(n => n.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        int number = 1;
        while (used.Contains($"Workspace {number}")) number++;
        return $"Workspace {number}";
    }
    public static bool DuplicateWorkspaceName(string name, IEnumerable<string> others) =>
        !string.IsNullOrWhiteSpace(name) && others.Any(n => string.Equals(n.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase));
    public static CloseAction Close(bool explicitExit, bool endingSession, bool loaded, bool trayReady) =>
        explicitExit || endingSession || !loaded ? CloseAction.Exit
        : trayReady ? CloseAction.Hide : CloseAction.Exit;

    // Delivery is synchronous, before hiding. A successful platform handoff does not promise a banner
    // (Windows can suppress it with Do not disturb). On failure the caller keeps the window open and shows
    // the explanation inline; that counts as shown (user 2026-11-14: "explain once, then hide"), so the next
    // close hides to the tray instead of keeping the window open forever when notifications are off.
    public static bool CanHideWithTrayNotice(AppSettings settings, Func<bool> deliver)
    {
        if (settings.TrayCloseNoticeShown) return true;
        bool delivered = deliver();
        settings.TrayCloseNoticeShown = true;
        return delivered;
    }

    public static bool CanSaveWindows(IEnumerable<(bool Selected, bool CanSelect)> windows) =>
        windows.Any(w => w.Selected && w.CanSelect);

    public static bool SelectWindow(bool selectAll, bool canSelect) => selectAll && canSelect;
    public static bool CanStartFocus(bool running, bool blockAll, int blockedCount) =>
        !running && (blockAll || blockedCount > 0);
    public static bool ConfirmFocusEnd(bool hasSession, bool strict, bool exiting) =>
        hasSession && (strict || exiting);
}
