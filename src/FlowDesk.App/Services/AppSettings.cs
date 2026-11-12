namespace FlowDesk.App.Services;

/// <summary>
/// Plain app settings persisted beside the workspace file. Lists are id maps so the frozen core model
/// does not need per-workspace flags: a missing id means the default (autosave on, Always add off).
/// </summary>
public sealed class AppSettings
{
    public const int MinLaunchTimeoutSeconds = LaunchTimeoutValue.Min;
    public const int MaxLaunchTimeoutSeconds = LaunchTimeoutValue.Max;
    public const int DefaultLaunchTimeoutSeconds = LaunchTimeoutValue.Default;

    public string Theme { get; set; } = "System";
    public bool MinimizePrevious { get; set; } = true;
    /// <summary>How long to wait for a launched app's window, in seconds (Settings: 10-60).</summary>
    public int LaunchTimeoutSeconds { get; set; } = DefaultLaunchTimeoutSeconds;
    public int Width { get; set; } = 1100;
    public int Height { get; set; } = 720;
    public int? X { get; set; }
    public int? Y { get; set; }
    /// <summary>The workspace whose last Open succeeded; resumed on startup. Null means none.</summary>
    public string? ActiveWorkspaceId { get; set; }
    public Dictionary<string, string> FocusIconPaths { get; set; } = [];

    /// <summary>Workspace ids whose "Keep this workspace up to date" toggle is off.</summary>
    public List<string> AutosaveOff { get; set; } = [];
    /// <summary>Workspace ids that add newly opened windows without asking.</summary>
    public List<string> AlwaysAddNewWindows { get; set; } = [];
    /// <summary>Browser notice keys the user has acknowledged (one per browser family).</summary>
    public List<string> BrowserRestoreNoticesSeen { get; set; } = [];

    public static int ClampLaunchTimeout(int seconds) => LaunchTimeoutValue.Clamp(seconds);

    public bool IsAutosaveEnabled(string workspaceId) =>
        !(AutosaveOff ?? []).Contains(workspaceId, StringComparer.Ordinal);

    public void SetAutosaveEnabled(string workspaceId, bool enabled)
    {
        AutosaveOff ??= [];
        bool off = AutosaveOff.Contains(workspaceId, StringComparer.Ordinal);
        if (enabled && off) AutosaveOff.RemoveAll(id => string.Equals(id, workspaceId, StringComparison.Ordinal));
        else if (!enabled && !off) AutosaveOff.Add(workspaceId);
    }

    public bool AlwaysAddsNewWindows(string workspaceId) =>
        (AlwaysAddNewWindows ?? []).Contains(workspaceId, StringComparer.Ordinal);

    public void SetAlwaysAddNewWindows(string workspaceId, bool always)
    {
        AlwaysAddNewWindows ??= [];
        bool on = AlwaysAddNewWindows.Contains(workspaceId, StringComparer.Ordinal);
        if (always && !on) AlwaysAddNewWindows.Add(workspaceId);
        else if (!always && on) AlwaysAddNewWindows.RemoveAll(id => string.Equals(id, workspaceId, StringComparison.Ordinal));
    }

    /// <summary>Clears the stored startup workspace only when it is the one being deleted.</summary>
    public bool ClearActiveWorkspaceIfMatches(string workspaceId)
    {
        if (!string.Equals(ActiveWorkspaceId, workspaceId, StringComparison.Ordinal)) return false;
        ActiveWorkspaceId = null;
        return true;
    }

    public bool HasSeenBrowserNotice(string key) =>
        (BrowserRestoreNoticesSeen ?? []).Contains(key, StringComparer.Ordinal);

    public void MarkBrowserNoticeSeen(string key)
    {
        BrowserRestoreNoticesSeen ??= [];
        if (string.IsNullOrEmpty(key) || BrowserRestoreNoticesSeen.Contains(key, StringComparer.Ordinal)) return;
        BrowserRestoreNoticesSeen.Add(key);
    }
}
