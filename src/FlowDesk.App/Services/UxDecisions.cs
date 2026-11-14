namespace FlowDesk.App.Services;

public enum CloseAction { Exit, Hide }

/// <summary>Pure decisions shared by the live UI and headless regression tests.</summary>
public static class UxDecisions
{
    public const bool InitialWindowSelection = false;
    public static CloseAction Close(bool explicitExit, bool endingSession, bool loaded, bool trayReady) =>
        explicitExit || endingSession || !loaded ? CloseAction.Exit
        : trayReady ? CloseAction.Hide : CloseAction.Exit;

    // Delivery is synchronous, before hiding: failure must leave both the window and the notice pending.
    // A successful platform handoff does not promise a banner (Windows can suppress it with Do not disturb).
    public static bool CanHideWithTrayNotice(AppSettings settings, Func<bool> deliver)
    {
        if (settings.TrayCloseNoticeShown) return true;
        if (!deliver()) return false;
        settings.TrayCloseNoticeShown = true;
        return true;
    }

    public static bool CanSaveWindows(IEnumerable<(bool Selected, bool CanSelect)> windows) =>
        windows.Any(w => w.Selected && w.CanSelect);

    public static bool SelectWindow(bool selectAll, bool canSelect) => selectAll && canSelect;
    public static bool CanStartFocus(bool running, bool blockAll, int blockedCount) =>
        !running && (blockAll || blockedCount > 0);
    public static bool ConfirmFocusEnd(bool hasSession, bool strict, bool exiting) =>
        hasSession && (strict || exiting);
}
