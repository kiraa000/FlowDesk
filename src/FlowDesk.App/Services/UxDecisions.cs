namespace FlowDesk.App.Services;

public enum CloseAction { Exit, Hide }

/// <summary>Pure decisions shared by the live UI and headless regression tests.</summary>
public static class UxDecisions
{
    public const bool InitialWindowSelection = false;
    public static CloseAction Close(bool explicitExit, bool endingSession, bool loaded, bool trayReady) =>
        explicitExit || endingSession || !loaded ? CloseAction.Exit
        : trayReady ? CloseAction.Hide : CloseAction.Exit;

    public static bool TakeTrayNotice(AppSettings settings)
    {
        if (settings.TrayCloseNoticeShown) return false;
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
