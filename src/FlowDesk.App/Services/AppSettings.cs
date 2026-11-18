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
    /// <summary>Optional global Alt window shortcuts. Off by default, also for files written before this existed.</summary>
    public bool WindowShortcutsEnabled { get; set; }
    public bool AdvancedModeEnabled { get; set; }
    public DateTimeOffset? AdvancedModeAgreedAt { get; set; }
    public List<string> DisabledShortcuts { get; set; } = [];
    [System.Text.Json.Serialization.JsonConverter(typeof(ShortcutOverridesConverter))]
    public Dictionary<string, string> ShortcutOverrides { get; set; } = [];
    public FlowDesk.Core.Hotkeys.HotkeyBinding EffectiveShortcut(string id) =>
        FlowDesk.Core.Hotkeys.ShortcutRebinding.Effective(ShortcutOverrides).Single(b => b.Id == id);
    // One preference shared with the Hotkeys tab; no independent switch that can drift out of sync.
    [System.Text.Json.Serialization.JsonIgnore]
    public bool CommandPaletteEnabled
    {
        get => !(DisabledShortcuts ?? []).Contains(FlowDesk.Core.Hotkeys.HotkeyCatalog.PaletteId);
        set
        {
            DisabledShortcuts ??= [];
            DisabledShortcuts.RemoveAll(id => id == FlowDesk.Core.Hotkeys.HotkeyCatalog.PaletteId);
            if (!value) DisabledShortcuts.Add(FlowDesk.Core.Hotkeys.HotkeyCatalog.PaletteId);
        }
    }
    /// <summary>Plain Space previews in Explorer/desktop; opt-in, including for older settings files.</summary>
    public bool QuickLookEnabled { get; set; }
    /// <summary>Opt-in, memory-only clipboard history; absent in old files means off.</summary>
    public bool ClipboardHistoryEnabled { get; set; }
    public bool PaletteWindowsKey { get; set; }
    public bool PaletteWindowsEnabled { get; set; } = true;
    public bool PaletteFilesEnabled { get; set; } = true;
    public bool PaletteEdgeBookmarks { get; set; } = true;
    public bool PaletteChromeBookmarks { get; set; } = true;
    public bool PaletteFirefoxBookmarks { get; set; } = true;
    public FlowDesk.Core.Palette.CustomCommandSettings CustomCommands { get; set; } = new();
    public FlowDesk.Core.Palette.AiOptions Ai { get; set; } = new();
    public bool PaletteCalculatorEnabled { get; set; } = true;
    public bool PaletteSystemEnabled { get; set; } = true;
    public bool PaletteWebEnabled { get; set; } = true;
    public bool PaletteWebFallback { get; set; } = true;
    public FlowDesk.Core.Palette.WebEngine PaletteWebEngine { get; set; }
    public string PaletteWebTemplate { get; set; } = "";
    public bool HotCornersEnabled { get; set; }
    public FlowDesk.Core.HotCorners.HotCornerAction HotCornerTopLeft { get; set; } = FlowDesk.Core.HotCorners.HotCornerAction.TaskView;
    public FlowDesk.Core.HotCorners.HotCornerAction HotCornerTopRight { get; set; }
    public FlowDesk.Core.HotCorners.HotCornerAction HotCornerBottomLeft { get; set; }
    public FlowDesk.Core.HotCorners.HotCornerAction HotCornerBottomRight { get; set; } = FlowDesk.Core.HotCorners.HotCornerAction.ShowDesktop;
    /// <summary>Empty uses Windows Terminal's app execution alias, falling back to Command Prompt.</summary>
    public string TerminalProgram { get; set; } = "";
    public bool TrayCloseNoticeShown { get; set; }
    public bool FirstCalmCheckDone { get; set; }

    public static int ClampLaunchTimeout(int seconds) => LaunchTimeoutValue.Clamp(seconds);

    /// <summary>
    /// Reads app-settings.json with the same options it is written with (enums as strings, e.g. hot corner
    /// actions). Reading with default options failed on every file saved since hot corners existed.
    /// </summary>
    public static AppSettings FromJson(string json) =>
        System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json, FlowDesk.Core.Storage.JsonStore.Options) ?? new();

    public string ToJson() => System.Text.Json.JsonSerializer.Serialize(this, FlowDesk.Core.Storage.JsonStore.Options);

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

/// <summary>Bad override values must not invalidate unrelated settings in an otherwise valid file.</summary>
public sealed class ShortcutOverridesConverter : System.Text.Json.Serialization.JsonConverter<Dictionary<string, string>>
{
    public override bool HandleNull => true;
    public override Dictionary<string, string> Read(ref System.Text.Json.Utf8JsonReader reader, Type type, System.Text.Json.JsonSerializerOptions options)
    {
        using var doc = System.Text.Json.JsonDocument.ParseValue(ref reader);
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object) { values["invalid-override-container"] = ""; return values; }
        foreach (var p in doc.RootElement.EnumerateObject())
            values[p.Name] = p.Value.ValueKind == System.Text.Json.JsonValueKind.String ? p.Value.GetString() ?? "" : "";
        return values;
    }
    public override void Write(System.Text.Json.Utf8JsonWriter writer, Dictionary<string, string> value, System.Text.Json.JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var p in FlowDesk.Core.Hotkeys.ShortcutRebinding.Normalize(value)) writer.WriteString(p.Key, p.Value);
        writer.WriteEndObject();
    }
}
