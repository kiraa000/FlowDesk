using FlowDesk.Core.Model;

namespace FlowDesk.App.Services;

public enum StartupResumeOutcome { NothingStored, Resumed, ClearedMissing, Failed }

/// <summary>
/// Resumes autosave for the workspace stored by the last successful Open. It never restores, launches or moves
/// a window: the workspace is only marked active and autosave is activated with the handles open right now.
/// Delegates keep the decision and its side effects testable without a desktop or the WinUI app.
/// </summary>
public static class StartupResume
{
    public static async Task<StartupResumeOutcome> TryResumeAsync(
        string? storedId,
        Func<string, Workspace?> findWorkspace,
        Func<Task<IReadOnlyList<long>>> enumerateBaseline,
        Func<string, bool> isAutosaveEnabled,
        Func<string, bool> alwaysAddsNewWindows,
        Action<string> markActive,
        Action<string> setActiveWorkspace,
        Action<string, IReadOnlyList<long>, bool, bool, DateTimeOffset?> activate,
        Action clearStoredId,
        Action<string> reportError)
    {
        if (string.IsNullOrWhiteSpace(storedId)) return StartupResumeOutcome.NothingStored;

        var workspace = findWorkspace(storedId);
        if (workspace is null)
        {
            clearStoredId();
            return StartupResumeOutcome.ClearedMissing;
        }

        try
        {
            // Read handles off the UI thread before anything becomes active, exactly like Open does after a restore.
            var baseline = await enumerateBaseline();
            markActive(workspace.Id);
            setActiveWorkspace(workspace.Id);
            activate(workspace.Id, baseline,
                isAutosaveEnabled(workspace.Id), alwaysAddsNewWindows(workspace.Id), workspace.LayoutSavedAt);
            return StartupResumeOutcome.Resumed;
        }
        catch (Exception ex)
        {
            reportError("Autosave could not resume for " + workspace.Name + ". " + ex.Message);
            return StartupResumeOutcome.Failed;
        }
    }
}
