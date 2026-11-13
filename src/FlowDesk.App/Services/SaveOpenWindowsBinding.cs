using FlowDesk.Core.Model;
using FlowDesk.Core.Restore;
using FlowDesk.Core.Windows;

namespace FlowDesk.App.Services;

/// <summary>
/// "Save open windows" remembers which live window each saved item came from, so the first Open reuses
/// exactly those windows instead of guessing between identical titles (e.g. two "Untitled - Paint").
/// Website items are not bound: restoring them opens their links rather than reusing an old browser window.
/// </summary>
public static class SaveOpenWindowsBinding
{
    /// <summary>Binds the windows of a just-saved workspace. Returns how many items were bound.</summary>
    public static int Bind(SessionTracker tracker, Workspace workspace,
        IEnumerable<(WorkspaceItem Item, WindowInfo Window)> saved)
    {
        int bound = 0;
        foreach (var (item, window) in saved)
        {
            if (item.Kind == ItemKind.Website) continue;
            tracker.Bind(workspace.Id, item.Id, window);
            bound++;
        }
        return bound;
    }
}
