using System.Text.Json;
using System.Text.Json.Nodes;
using FlowDesk.Core;
using FlowDesk.Core.Hotkeys;
using FlowDesk.Core.Model;
using FlowDesk.Core.Palette;
using FlowDesk.Core.Storage;

namespace FlowDesk.App.Services;

public sealed record TransferSelection(bool Workspaces = true, bool Focus = true, bool Settings = true,
    bool CustomCommands = true, bool Replace = false);
public sealed record TransferSection(string Id, string Summary, string? Error = null);
public sealed class TransferPreview
{
    public IReadOnlyList<TransferSection> Sections { get; internal set; } = [];
    public string? Error { get; internal set; }
    internal WorkspaceTransfer? Workspaces { get; set; }
    internal FocusSettings? Focus { get; set; }
    internal JsonObject? Settings { get; set; }
    internal CustomCommandSettings? CustomCommands { get; set; }
}
public sealed class WorkspaceTransfer
{
    public List<Workspace> Items { get; set; } = [];
    public List<string> AutosaveOff { get; set; } = [];
    public List<string> AlwaysAddNewWindows { get; set; } = [];
}
public sealed record TransferPlan(FlowDeskData Data, AppSettings Settings, IReadOnlyList<string> Warnings);

/// <summary>Inert, bounded import parsing and pure merge rules. Nothing here launches a file or reads credentials.</summary>
public static class SettingsTransfer
{
    public const int MaxBytes = 5 * 1024 * 1024;
    public const string NewerMessage = "This file comes from a newer FlowDesk. Update FlowDesk to import it.";
    private static readonly JsonSerializerOptions ExportOptions = new(JsonStore.Options) { IgnoreReadOnlyProperties = true };
    // Deliberately explicit: adding an AppSettings property never opts it into transfer.
    private static readonly string[] Allowed = [
        "Theme", "MinimizePrevious", "LaunchTimeoutSeconds", "WindowShortcutsEnabled", "DisabledShortcuts", "ShortcutOverrides",
        "AutoGameMode", "WindowMaximized", "LastPage",
        "QuickLookEnabled", "ClipboardHistoryEnabled", "PaletteWindowsKey", "PaletteWindowsEnabled", "PaletteFilesEnabled",
        "PaletteEdgeBookmarks", "PaletteChromeBookmarks", "PaletteFirefoxBookmarks", "PaletteCalculatorEnabled", "PaletteSystemEnabled",
        "PaletteWebEnabled", "PaletteWebFallback", "PaletteWebEngine", "PaletteWebTemplate", "HotCornersEnabled", "HotCornerTopLeft",
        "HotCornerTopRight", "HotCornerBottomLeft", "HotCornerBottomRight", "TerminalProgram"];
    private static readonly string[] AiAllowed = ["Provider", "BaseUrl", "Model", "ShowFallback"];
    private static JsonObject Object<T>(T value) => JsonSerializer.SerializeToNode(value, ExportOptions)!.AsObject();
    private static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, JsonStore.Options), JsonStore.Options)!;
    private static JsonObject Filter(JsonObject source)
    {
        var result = new JsonObject();
        foreach (var name in Allowed) if (source.TryGetPropertyValue(name, out var node)) result[name] = node?.DeepClone();
        if (source.TryGetPropertyValue("Ai", out var ai))
        {
            if (ai is not JsonObject obj) throw new JsonException();
            var safe = new JsonObject();
            foreach (var name in AiAllowed) if (obj.TryGetPropertyValue(name, out var node)) safe[name] = node?.DeepClone();
            result["Ai"] = safe;
        }
        return result;
    }
    public static string Export(FlowDeskData data, AppSettings settings, TransferSelection selection, string appVersion,
        DateTimeOffset? now = null)
    {
        var sections = new JsonObject();
        if (selection.Workspaces)
        {
            var ids = data.Workspaces.Select(w => w.Id).ToHashSet(StringComparer.Ordinal);
            sections["workspaces"] = JsonSerializer.SerializeToNode(new WorkspaceTransfer { Items = data.Workspaces,
                AutosaveOff = settings.AutosaveOff.Where(ids.Contains).ToList(),
                AlwaysAddNewWindows = settings.AlwaysAddNewWindows.Where(ids.Contains).ToList() }, ExportOptions);
        }
        if (selection.Focus) sections["focus"] = JsonSerializer.SerializeToNode(data.Focus, ExportOptions);
        if (selection.Settings)
        {
            var source = Object(settings);
            var ai = settings.Ai ?? new AiOptions();
            source["Ai"] = Object(ai);
            var safe = Filter(source);
            // The normal editor forbids credentials/query tokens in endpoint URLs. Defend old/hand-edited stores too.
            if (AiRules.Validate(ai with { Provider = AiProviderKind.OpenAICompatible }) is not null)
                safe["Ai"]!["BaseUrl"] = new AiOptions().BaseUrl;
            sections["settings"] = safe;
        }
        if (selection.CustomCommands) sections["customCommands"] = JsonSerializer.SerializeToNode(settings.CustomCommands, ExportOptions);
        return new JsonObject { ["format"] = "flowdesk-export", ["version"] = 1, ["appVersion"] = appVersion,
            ["exportedAt"] = (now ?? DateTimeOffset.UtcNow).ToString("O"), ["sections"] = sections }.ToJsonString(ExportOptions);
    }
    public static async Task<TransferPreview> ReadAsync(string path)
    {
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > MaxBytes) return Failed("This file is larger than 5 MB. Choose a smaller FlowDesk export.");
            // Bounded even if another process grows the file while it is read.
            var bytes = new byte[MaxBytes + 1]; int count = 0, read;
            while (count < bytes.Length && (read = await stream.ReadAsync(bytes.AsMemory(count))) > 0) count += read;
            return Parse(bytes.AsMemory(0, count));
        }
        catch (Exception) { return Failed("This file could not be read. Check access and try again."); }
    }
    private static TransferPreview Failed(string message) => new() { Error = message };
    public static TransferPreview Parse(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Length > MaxBytes) return Failed("This file is larger than 5 MB. Choose a smaller FlowDesk export.");
        try
        {
            if (bytes.Span.StartsWith(new byte[] { 0xef, 0xbb, 0xbf })) bytes = bytes[3..];
            using var doc = JsonDocument.Parse(bytes);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("format", out var format) || format.ValueKind != JsonValueKind.String || format.GetString() != "flowdesk-export")
                return Failed("This is not a FlowDesk settings export.");
            if (!root.TryGetProperty("version", out var version) || !version.TryGetInt32(out int number) || number < 1)
                return Failed("This export has an invalid version.");
            if (number > 1) return Failed(NewerMessage);
            if (!root.TryGetProperty("sections", out var sections) || sections.ValueKind != JsonValueKind.Object)
                return Failed("This export has no valid sections.");
            var preview = new TransferPreview(); var rows = new List<TransferSection>();
            void Read(string id, Func<JsonElement, string> parse)
            {
                if (!sections.TryGetProperty(id, out var section)) return;
                try { rows.Add(new(id, parse(section))); }
                catch (Exception ex) when (ex is JsonException or InvalidDataException or ArgumentException or InvalidOperationException or NullReferenceException)
                { rows.Add(new(id, Label(id), "This section is damaged and cannot be imported.")); }
            }
            Read("workspaces", node =>
            {
                var value = node.Deserialize<WorkspaceTransfer>(JsonStore.Options) ?? throw new JsonException();
                if (value.Items is null || value.AutosaveOff is null || value.AlwaysAddNewWindows is null ||
                    value.Items.Any(w => w is null || string.IsNullOrWhiteSpace(w.Id) || string.IsNullOrWhiteSpace(w.Name) || w.Description is null || w.Glyph is null || w.Color is null || w.Items is null ||
                        w.Items.Any(i => i is null || string.IsNullOrWhiteSpace(i.Id) || i.Urls is null || i.Urls.Any(u => u is null) || !Enum.IsDefined(i.Kind) || i.Name is null ||
                            (i.App is not null && i.App.ExePath is null) ||
                            (i.Window is not null && (!Enum.IsDefined(i.Window.Show) || i.Window.Title is null || i.Window.ClassName is null || i.Window.MonitorDevice is null ||
                                i.Window.MonitorStableId is null || i.Window.MonitorName is null))))) throw new JsonException();
                if (value.Items.Any(w => w.Items.Select(i => i.Id).Distinct(StringComparer.Ordinal).Count() != w.Items.Count)) throw new JsonException();
                if (value.Items.Select(w => w.Id).Distinct(StringComparer.Ordinal).Count() != value.Items.Count) throw new JsonException();
                preview.Workspaces = value; return CountText.Format(value.Items.Count, "workspace");
            });
            Read("focus", node =>
            {
                var value = node.Deserialize<FocusSettings>(JsonStore.Options) ?? throw new JsonException();
                if (value.Blocked is null || value.Allowed is null || value.Blocked.Concat(value.Allowed).Any(r => r is null || r.Name is null) ||
                    !Enum.IsDefined(value.Strictness) || !FocusSettings.DurationChoices.Contains(value.DurationMinutes)) throw new JsonException();
                preview.Focus = value; return "Focus settings";
            });
            Read("settings", node =>
            {
                var safe = Filter(JsonNode.Parse(node.GetRawText())!.AsObject());
                if (safe.TryGetPropertyValue("ShortcutOverrides", out var overrides) && overrides is not JsonObject) throw new JsonException();
                var value = safe.Deserialize<AppSettings>(JsonStore.Options) ?? throw new JsonException();
                if (value.LastPage is not ("Workspaces" or "Capture" or "Settings" or "Layouts" or "Advanced" or "Focus" or "Calm" or "Hotkeys" or "Details")) throw new JsonException();
                if (value.Theme is not ("System" or "Light" or "Dark") || value.DisabledShortcuts is null || value.ShortcutOverrides is null || value.Ai is null ||
                    !Enum.IsDefined(value.PaletteWebEngine) || !Enum.IsDefined(value.Ai.Provider) || value.TerminalProgram is null || value.PaletteWebTemplate is null ||
                    !Enum.IsDefined(value.HotCornerTopLeft) || !Enum.IsDefined(value.HotCornerTopRight) || !Enum.IsDefined(value.HotCornerBottomLeft) || !Enum.IsDefined(value.HotCornerBottomRight) ||
                    AiRules.Validate(value.Ai with { Provider = AiProviderKind.OpenAICompatible }) is not null) throw new JsonException();
                preview.Settings = safe; int changes = value.ShortcutOverrides.Count + value.DisabledShortcuts.Count; return changes == 0 ? "Settings and shortcuts" : "Settings and " + CountText.Format(changes, "shortcut change");
            });
            Read("customCommands", node =>
            {
                var value = node.Deserialize<CustomCommandSettings>(JsonStore.Options) ?? throw new JsonException();
                if (value.Version != 1 || value.Entries is null || value.Entries.Any(c => c is null)) throw new JsonException();
                preview.CustomCommands = value; return value.Entries.Count == 0 ? "Custom palette commands (none)" : CountText.Format(value.Entries.Count, "custom command");
            });
            preview.Sections = rows;
            if (rows.Count == 0) preview.Error = "This export has no supported sections.";
            return preview;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        { return Failed("This file is not valid FlowDesk JSON."); }
    }
    private static string Label(string id) => id switch { "workspaces" => "Workspaces and layouts", "focus" => "Focus settings", "settings" => "Settings and shortcuts", _ => "Custom palette commands" };

    public static Task<TransferPlan> PrepareAsync(FlowDeskData current, AppSettings local, TransferPreview preview, TransferSelection selection)
    {
        // Snapshot dispatcher-owned state before doing any local filesystem checks off-thread.
        var data = Copy(current); var settings = Copy(local);
        return Task.Run(() => Merge(data, settings, preview, selection));
    }

    private static bool DrivePath(string path) => path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] is '\\' or '/';
    private static bool ExistsLocal(string path) => LocalPath(path, allowMissing: false);
    private static bool SafeWorkspacePath(string path) => LocalPath(path, allowMissing: true);
    private static bool LocalPath(string path, bool allowMissing)
    {
        // Never authenticate to an imported UNC/device/mapped-network path. Also refuse reparse ancestors
        // (junctions/symlinks/cloud placeholders) before traversing them: a local-looking path may lead to SMB.
        try
        {
            string full = Path.GetFullPath(path); string root = Path.GetPathRoot(full)!;
            if (new DriveInfo(root).DriveType is not (DriveType.Fixed or DriveType.Removable or DriveType.CDRom or DriveType.Ram)) return false;
            string cursor = root;
            foreach (string part in full[root.Length..].Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries))
            {
                cursor = Path.Combine(cursor, part);
                try { if ((File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0) return false; }
                catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
                { return allowMissing; } // Existing ancestors were local and non-reparse; keep portable/missing items.
            }
            return File.Exists(full) || Directory.Exists(full);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return false; }
    }

    public static TransferPlan Merge(FlowDeskData current, AppSettings local, TransferPreview preview, TransferSelection selection,
        Func<string, bool>? exists = null)
    {
        if (preview.Error is not null) throw new InvalidDataException(preview.Error);
        var data = Copy(current); var settings = Copy(local); var warnings = new List<string>();
        if (selection.Settings && preview.Settings is { } importedSettings)
        {
            var overlay = Object(settings);
            foreach (var pair in importedSettings) overlay[pair.Key] = pair.Value?.DeepClone();
            settings = overlay.Deserialize<AppSettings>(JsonStore.Options)!;
            if (settings.Ai.Provider != local.Ai.Provider || settings.Ai.BaseUrl != local.Ai.BaseUrl)
                warnings.Add("AI endpoint was not imported; set it in Settings if you want it");
            settings.Ai = settings.Ai with { Provider = local.Ai.Provider, BaseUrl = local.Ai.BaseUrl };
            settings.LaunchTimeoutSeconds = AppSettings.ClampLaunchTimeout(settings.LaunchTimeoutSeconds);
            settings.DisabledShortcuts = new ShortcutPolicy(settings.DisabledShortcuts).DisabledIds.ToList();
            settings.ShortcutOverrides = ResolveShortcuts(importedSettings["ShortcutOverrides"] as JsonObject ?? Object(settings)["ShortcutOverrides"] as JsonObject, settings, warnings);
        }
        if (selection.Workspaces && preview.Workspaces is { } workspaces)
        {
            if (selection.Replace) { data.Workspaces.Clear(); settings.AutosaveOff.Clear(); settings.AlwaysAddNewWindows.Clear(); }
            var ids = data.Workspaces.Select(w => w.Id).ToHashSet(StringComparer.Ordinal);
            var names = data.Workspaces.Select(w => w.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var source in workspaces.Items)
            {
                var w = source.Clone();
                w.Items.RemoveAll(item =>
                {
                    var paths = new List<string>();
                    if (!string.IsNullOrEmpty(item.App?.ExePath)) paths.Add(item.App.ExePath);
                    if (!string.IsNullOrEmpty(item.App?.WorkingDirectory)) paths.Add(item.App.WorkingDirectory);
                    if (item.Kind is ItemKind.File or ItemKind.Folder) paths.Add(item.Path ?? "");
                    if (paths.All(p => DrivePath(Environment.ExpandEnvironmentVariables(p)) && SafeWorkspacePath(Environment.ExpandEnvironmentVariables(p)))) return false;
                    warnings.Add($"Workspace item '{item.Name}' skipped: its path is not a safe local path.");
                    return true;
                });
                while (!ids.Add(w.Id)) w.Id = WorkspaceItem.NewId();
                if (!names.Add(w.Name))
                {
                    var stem = w.Name + " (imported)"; w.Name = stem; int n = 2;
                    while (!names.Add(w.Name)) w.Name = stem + " " + n++;
                }
                data.Workspaces.Add(w);
                if (workspaces.AutosaveOff.Contains(source.Id)) settings.SetAutosaveEnabled(w.Id, false);
                if (workspaces.AlwaysAddNewWindows.Contains(source.Id)) settings.SetAlwaysAddNewWindows(w.Id, true);
            }
            if (selection.Replace) settings.ActiveWorkspaceId = null; // never attach current windows to imported identities
        }
        if (selection.Focus && preview.Focus is { } focus) data.Focus = Copy(focus);
        if (selection.CustomCommands && preview.CustomCommands is { } commands)
        {
            var entries = selection.Replace ? new List<CustomCommandDefinition>() : settings.CustomCommands.Entries.ToList();
            foreach (var source in commands.Entries)
            {
                var c = source;
                // Validate ignores matching ids (editor semantics), so remap identity before checking keyword clashes.
                if (entries.Any(e => e.Id == c.Id)) c = c with { Id = Guid.NewGuid().ToString("N") };
                string target = Environment.ExpandEnvironmentVariables(c.Target ?? "");
                bool networkPath = c.Kind != CustomTargetKind.Url && (target.StartsWith(@"\\", StringComparison.Ordinal) || target.StartsWith("//", StringComparison.Ordinal));
                string? error = networkPath ? "Network paths are not supported." : CustomCommandRules.Validate(c, entries, p => DrivePath(p) && (exists ?? ExistsLocal)(p));
                if (error is not null) { warnings.Add($"Custom command '{c.Name}' skipped: {error}"); continue; }
                entries.Add(c);
            }
            settings.CustomCommands = new() { Entries = entries };
        }
        return new(data, settings, warnings);
    }
    private static Dictionary<string, string> ResolveShortcuts(JsonObject? raw, AppSettings settings, List<string> warnings)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in raw ?? new JsonObject())
        {
            string? text = pair.Value is JsonValue value && value.TryGetValue<string>(out var s) ? s : null;
            if (HotkeyCatalog.Find(pair.Key) is not { IsMouseWheel: false } || !KeyChord.TryParse(text ?? "", out var chord))
            { warnings.Add($"Shortcut '{pair.Key}' could not be used; the default was kept."); continue; }
            try
            {
                var actions = new[] { settings.HotCornerTopLeft, settings.HotCornerTopRight, settings.HotCornerBottomLeft, settings.HotCornerBottomRight };
                var check = ShortcutRebinding.Evaluate(ShortcutRebinding.Effective(result), pair.Key, chord, settings.QuickLookEnabled,
                    settings.HotCornersEnabled && actions.Contains(FlowDesk.Core.HotCorners.HotCornerAction.ShowDesktop),
                    settings.HotCornersEnabled && actions.Contains(FlowDesk.Core.HotCorners.HotCornerAction.TaskView));
                if (check.Blocked) throw new InvalidOperationException();
                result[pair.Key] = chord.ToString();
            }
            catch (InvalidOperationException) { warnings.Add($"Shortcut '{pair.Key}' could not be used; the default was kept."); }
        }
        // Evaluate the complete graph, so valid swaps round-trip. Rejected overrides restore defaults;
        // repeat until those defaults no longer collide with another imported override.
        bool changed;
        do
        {
            changed = false;
            var table = ShortcutRebinding.Effective(result).Where(b => !b.IsMouseWheel && !settings.DisabledShortcuts.Contains(b.Id)).ToArray();
            foreach (var group in table.GroupBy(KeyChord.From).Where(g => g.Count() > 1))
                foreach (var binding in group)
                    if (result.Remove(binding.Id))
                    { changed = true; warnings.Add($"Shortcut '{binding.Id}' could not be used; the default was kept."); }
        } while (changed);
        return ShortcutRebinding.Normalize(result);
    }
}
