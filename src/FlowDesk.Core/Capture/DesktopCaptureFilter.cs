using FlowDesk.Core.Desktops;
using FlowDesk.Core.Windows;

namespace FlowDesk.Core.Capture;

public static class DesktopCaptureFilter
{
    /// <summary>
    /// A shell cloak alone is not proof of an app on another desktop. Require positive desktop
    /// identity and an explicit not-current answer; APP/INHERITED/mixed cloaks remain excluded.
    /// Other Alt-Tab checks (shell class, tool/owned/hidden windows) are still required separately.
    /// </summary>
    public static bool AllowCloaked(int reason, WindowDesktopInfo desktop, bool includeOtherDesktops = true) =>
        reason == 0 || (includeOtherDesktops && reason == 2 && desktop.IsCurrent == false
            && desktop.HasIdentity);

    /// <summary>Unknown + cloaked is not proof of being local. Old uncloaked metadata remains compatible.</summary>
    public static bool IsLocalForLayout(WindowInfo window) => window.IsOnCurrentDesktop == true
        || (window.IsOnCurrentDesktop is null && window.CloakReason == 0);
}
