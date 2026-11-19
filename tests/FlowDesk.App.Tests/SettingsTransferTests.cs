using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FlowDesk.App.Services;
using FlowDesk.Core.Hotkeys;
using FlowDesk.Core.Model;
using FlowDesk.Core.Palette;
using FlowDesk.Core.Storage;

namespace FlowDesk.App.Tests;

public sealed class SettingsTransferTests
{
    private static TransferPreview Parse(string text) => SettingsTransfer.Parse(Encoding.UTF8.GetBytes(text));
    private static string Envelope(string sections, int version = 1) =>
        "{\"format\":\"flowdesk-export\",\"version\":" + version + ",\"sections\":" + sections + "}";
    private static (FlowDeskData, AppSettings) Sample()
    {
        var data = new FlowDeskData { TourCompleted = true, Workspaces = [new() { Id = "work", Name = "Work",
            Context = new() { Focus = ContextFocus.Start, FocusMinutes = 50, DoNotDisturb = ContextToggle.On },
            Items = [new() { Name = "Editor", App = new() { ExePath = @"D:\missing\editor.exe", Arguments = "file.txt" },
                Window = new() { Rect = new(10, 20, 800, 600), Show = WindowShow.Maximized } }] }],
            Focus = new() { DurationMinutes = 50, Strictness = FocusStrictness.Gentle, Blocked = [new() { Name = "Game", ExeName = "game" }] } };
        var settings = new AppSettings { Theme = "Dark", Width = 999, X = 123, ActiveWorkspaceId = "work", AdvancedModeEnabled = true,
            AdvancedModeAgreedAt = DateTimeOffset.UtcNow, AutosaveOff = ["work", "not-exported"], AlwaysAddNewWindows = ["work"],
            FocusIconPaths = new() { ["Game"] = "private-icon" }, BrowserRestoreNoticesSeen = ["private-notice"], TrayCloseNoticeShown = true,
            FirstCalmCheckDone = true, LaunchTimeoutSeconds = 40, TerminalProgram = @"D:\terminal.exe",
            Ai = new() { Provider = AiProviderKind.OpenAICompatible, BaseUrl = "https://example.org/v1", Model = "model", ShowFallback = false },
            CustomCommands = new() { Entries = [new("c", "Search", "findme", CustomTargetKind.Url, "https://example.org/?q={query}")] },
            ShortcutOverrides = new() { [HotkeyCatalog.PaletteId] = "Ctrl+Alt+P" }, DisabledShortcuts = ["snap-left"] };
        return (data, settings);
    }
    [Fact] public void RoundTripReplacePreservesAllowedFieldsAndLocalConsent()
    {
        var (source, settings) = Sample();
        var json = SettingsTransfer.Export(source, settings, new(), "test"); var preview = Parse(json);
        Assert.Null(preview.Error); Assert.All(preview.Sections, s => Assert.Null(s.Error));
        var local = new AppSettings { Width = 777, X = 88, TrayCloseNoticeShown = true, Ai = settings.Ai };
        var plan = SettingsTransfer.Merge(new() { TourCompleted = false }, local, preview, new(Replace: true), _ => true);
        Assert.Equal(JsonSerializer.Serialize(source.Workspaces, JsonStore.Options), JsonSerializer.Serialize(plan.Data.Workspaces, JsonStore.Options));
        Assert.Equal(JsonSerializer.Serialize(source.Focus, JsonStore.Options), JsonSerializer.Serialize(plan.Data.Focus, JsonStore.Options));
        Assert.Equal("Dark", plan.Settings.Theme); Assert.Equal(777, plan.Settings.Width); Assert.Equal(88, plan.Settings.X);
        Assert.False(plan.Settings.AdvancedModeEnabled); Assert.Null(plan.Settings.AdvancedModeAgreedAt);
        Assert.False(plan.Data.TourCompleted); Assert.True(plan.Settings.TrayCloseNoticeShown); Assert.Null(plan.Settings.ActiveWorkspaceId);
        Assert.Equal(settings.Ai, plan.Settings.Ai); Assert.Equal(settings.TerminalProgram, plan.Settings.TerminalProgram);
        Assert.Equal(settings.ShortcutOverrides, plan.Settings.ShortcutOverrides); Assert.Equal(settings.DisabledShortcuts, plan.Settings.DisabledShortcuts);
        Assert.Equal(new[] { "work" }, plan.Settings.AutosaveOff); Assert.Equal(new[] { "work" }, plan.Settings.AlwaysAddNewWindows);
        Assert.Equal(settings.CustomCommands.Entries, plan.Settings.CustomCommands.Entries); Assert.Empty(plan.Warnings);
        Assert.Equal(SettingsTransfer.Export(source, settings, new(), "test", DateTimeOffset.UnixEpoch),
            SettingsTransfer.Export(plan.Data, plan.Settings, new(), "test", DateTimeOffset.UnixEpoch));
    }
    [Fact] public void SerializedAllowListDoesNotContainLocalStateOrComputedAppKey()
    {
        var (data, settings) = Sample();
        string json = SettingsTransfer.Export(data, settings, new(), "test");
        foreach (string name in new[] { "ActiveWorkspaceId", "AdvancedModeEnabled", "AdvancedModeAgreedAt", "FocusIconPaths", "TourCompleted",
            "BrowserRestoreNoticesSeen", "TrayCloseNoticeShown", "FirstCalmCheckDone", "Key", "ApiKey", "Credential", "CommandPaletteEnabled" })
            Assert.DoesNotContain("\"" + name + "\"", json, StringComparison.OrdinalIgnoreCase);
        var section = JsonNode.Parse(json)!["sections"]!["settings"]!.AsObject();
        foreach (string name in new[] { "Width", "Height", "X", "Y", "AutosaveOff", "AlwaysAddNewWindows", "CustomCommands" }) Assert.False(section.ContainsKey(name));
        Assert.DoesNotContain("private-icon", json); Assert.DoesNotContain("private-notice", json);
        Assert.Equal(800, JsonNode.Parse(json)!["sections"]!["workspaces"]!["Items"]![0]!["Items"]![0]!["Window"]!["Rect"]!["Width"]!.GetValue<int>());
    }
    [Fact] public void EverySectionCanBeOmitted()
    {
        var (data, settings) = Sample();
        var json = JsonNode.Parse(SettingsTransfer.Export(data, settings, new(false, false, false, false), "test"))!;
        Assert.Empty(json["sections"]!.AsObject()); Assert.Equal("flowdesk-export", json["format"]!.GetValue<string>());
    }
    [Theory]
    [InlineData("{", "This file is not valid FlowDesk JSON.")]
    [InlineData("[]", "This is not a FlowDesk settings export.")]
    [InlineData("{\"format\":\"wrong\"}", "This is not a FlowDesk settings export.")]
    [InlineData("{\"format\":\"flowdesk-export\",\"version\":0}", "This export has an invalid version.")]
    [InlineData("{\"format\":\"flowdesk-export\",\"version\":2}", SettingsTransfer.NewerMessage)]
    [InlineData("{\"format\":\"flowdesk-export\",\"version\":1}", "This export has no valid sections.")]
    public void InvalidEnvelopeRejected(string json, string error) => Assert.Equal(error, Parse(json).Error);
    [Fact] public void OversizeRejectedBeforeJsonParsing() => Assert.Contains("larger than 5 MB", SettingsTransfer.Parse(new byte[SettingsTransfer.MaxBytes + 1]).Error);
    [Fact] public void Utf8BomAccepted()
    {
        var bytes = new byte[] { 0xef, 0xbb, 0xbf }.Concat(Encoding.UTF8.GetBytes(Envelope("{\"focus\":{}}"))).ToArray();
        Assert.Null(SettingsTransfer.Parse(bytes).Error);
    }
    [Theory]
    [InlineData("workspaces", "{\"Items\":[null]}")]
    [InlineData("workspaces", "{\"Items\":[{\"Id\":\"a\",\"Name\":\"A\",\"Items\":null}]}")]
    [InlineData("focus", "{\"Blocked\":null}")]
    [InlineData("settings", "{\"Ai\":null}")]
    [InlineData("settings", "{\"Theme\":null}")]
    [InlineData("settings", "{\"ShortcutOverrides\":[]}")]
    [InlineData("customCommands", "{\"Entries\":[null]}")]
    public void BrokenSingleSectionIsIsolated(string id, string bad)
    {
        var sections = new JsonObject { [id] = JsonNode.Parse(bad), ["unknownFutureSection"] = new JsonObject() };
        if (id != "focus") sections["focus"] = new JsonObject();
        var preview = Parse(Envelope(sections.ToJsonString()));
        Assert.Null(preview.Error); Assert.NotNull(preview.Sections.Single(s => s.Id == id).Error);
        if (id != "focus") Assert.Null(preview.Sections.Single(s => s.Id == "focus").Error);
        var plan = SettingsTransfer.Merge(new(), new(), preview, new());
        Assert.Empty(plan.Data.Workspaces);
    }
    [Fact] public void ImportCannotSmuggleLocalFieldsThroughSettings()
    {
        var preview = Parse(Envelope("""{"settings":{"Theme":"Dark","Width":2000,"ActiveWorkspaceId":"evil","AdvancedModeEnabled":true,"AdvancedModeAgreedAt":"2026-01-01T00:00:00Z","CustomCommands":{"Entries":[]},"Ai":{"Provider":"OpenAICompatible","BaseUrl":"https://example.org","Model":"m","ShowFallback":false,"ApiKey":"SECRET"}}}"""));
        var local = new AppSettings { Width = 888, ActiveWorkspaceId = "local", CustomCommands = new() { Entries = [new("a", "A", "aaa", CustomTargetKind.Url, "https://example.org")] } };
        var plan = SettingsTransfer.Merge(new(), local, preview, new());
        Assert.Equal("Dark", plan.Settings.Theme); Assert.Equal(888, plan.Settings.Width); Assert.Equal("local", plan.Settings.ActiveWorkspaceId);
        Assert.False(plan.Settings.AdvancedModeEnabled); Assert.Null(plan.Settings.AdvancedModeAgreedAt); Assert.Single(plan.Settings.CustomCommands.Entries);
        Assert.False(plan.Settings.Ai.ShowFallback); Assert.DoesNotContain("SECRET", plan.Settings.ToJson());
    }
    [Fact] public void AddRemapsIdentityNameAndPerWorkspaceFlagsWithoutMutatingInput()
    {
        var (data, settings) = Sample(); var preview = Parse(SettingsTransfer.Export(data, settings, new(), "test"));
        var original = data.Workspaces[0];
        var plan = SettingsTransfer.Merge(data, settings, preview, new(CustomCommands: false));
        Assert.Equal(2, plan.Data.Workspaces.Count); var imported = plan.Data.Workspaces[1];
        Assert.NotEqual("work", imported.Id); Assert.Equal("Work (imported)", imported.Name);
        Assert.Contains(imported.Id, plan.Settings.AutosaveOff); Assert.Contains(imported.Id, plan.Settings.AlwaysAddNewWindows);
        Assert.Single(data.Workspaces); Assert.Same(original, data.Workspaces[0]); Assert.Equal("Work", preview.Workspaces!.Items[0].Name);
        Assert.Equal("work", plan.Settings.ActiveWorkspaceId);
    }
    [Fact] public void ReplaceRemovesOldIdsFlagsButNotLocalOnboarding()
    {
        var (data, settings) = Sample(); var preview = Parse(SettingsTransfer.Export(data, settings, new(), "test"));
        var local = new AppSettings { AutosaveOff = ["old"], AlwaysAddNewWindows = ["old"], ActiveWorkspaceId = "old", AdvancedModeEnabled = true };
        var plan = SettingsTransfer.Merge(new() { Workspaces = [new() { Id = "old", Name = "Old" }], TourCompleted = true }, local, preview, new(Replace: true));
        Assert.Single(plan.Data.Workspaces); Assert.Equal("work", plan.Data.Workspaces[0].Id); Assert.True(plan.Data.TourCompleted);
        Assert.DoesNotContain("old", plan.Settings.AutosaveOff); Assert.True(plan.Settings.AdvancedModeEnabled);
    }
    [Fact] public void MissingWorkspacePathsAreKeptButMissingCustomCommandIsSkipped()
    {
        var (data, settings) = Sample(); settings.CustomCommands.Entries.Add(new("missing", "Missing", "missing", CustomTargetKind.Program, @"D:\not-here.exe"));
        var plan = SettingsTransfer.Merge(new(), new(), Parse(SettingsTransfer.Export(data, settings, new(), "test")), new(Replace: true), _ => false);
        Assert.Equal(@"D:\missing\editor.exe", plan.Data.Workspaces[0].Items[0].App!.ExePath);
        Assert.Single(plan.Settings.CustomCommands.Entries); Assert.Contains(plan.Warnings, w => w.Contains("Missing") && w.Contains("skipped"));
    }
    [Fact] public void InvalidAndClashingCustomCommandsAreListedIncludingSameIdClash()
    {
        var local = new AppSettings { CustomCommands = new() { Entries = [new("same", "Existing", "shared", CustomTargetKind.Url, "https://example.org")] } };
        var imported = new AppSettings { CustomCommands = new() { Entries = [new("same", "Clash", "shared", CustomTargetKind.Url, "https://example.org"),
            new("bad", "Bad", "help", CustomTargetKind.Url, "https://example.org"), new("good", "Good", "good", CustomTargetKind.Url, "https://example.org")] } };
        var plan = SettingsTransfer.Merge(new(), local, Parse(SettingsTransfer.Export(new(), imported, new(), "test")), new());
        Assert.Equal(2, plan.Settings.CustomCommands.Entries.Count); Assert.Equal(2, plan.Warnings.Count);
        Assert.Contains(plan.Warnings, w => w.Contains("Clash")); Assert.Contains(plan.Warnings, w => w.Contains("Bad"));
    }
    [Theory]
    [InlineData("Alt+A")]
    [InlineData("Win+L")]
    [InlineData("not keys")]
    public void ConflictingOrInvalidShortcutReportedAndDefaultKept(string text)
    {
        var preview = Parse(Envelope("{\"settings\":{\"ShortcutOverrides\":{\"command-palette\":" + JsonSerializer.Serialize(text) + "}}}"));
        var plan = SettingsTransfer.Merge(new(), new(), preview, new());
        Assert.Empty(plan.Settings.ShortcutOverrides); Assert.Single(plan.Warnings);
        Assert.Contains("default was kept", plan.Warnings[0]);
        Assert.Equal("Win+Alt+Space", plan.Settings.EffectiveShortcut(HotkeyCatalog.PaletteId).Shortcut);
    }
    [Fact] public void ValidShortcutSwapRoundTrips()
    {
        var s = new AppSettings { ShortcutOverrides = new() { ["snap-left"] = "Alt+D", ["snap-right"] = "Alt+A" } };
        var plan = SettingsTransfer.Merge(new(), new(), Parse(SettingsTransfer.Export(new(), s, new(), "test")), new());
        Assert.Equal(s.ShortcutOverrides, plan.Settings.ShortcutOverrides); Assert.Empty(plan.Warnings);
    }
    [Fact] public void DeselectingAllSectionsLeavesDataEqual()
    {
        var (data, settings) = Sample();
        var plan = SettingsTransfer.Merge(data, settings, Parse(SettingsTransfer.Export(new(), new(), new(), "test")), new(false, false, false, false, true));
        Assert.Equal(JsonSerializer.Serialize(data, JsonStore.Options), JsonSerializer.Serialize(plan.Data, JsonStore.Options));
        Assert.Equal(settings.ToJson(), plan.Settings.ToJson());
    }
    [Fact] public async Task BackupsPruneOnlyOwnedFoldersAndUndoRestoresCapturedBytes()
    {
        string root = Path.Combine(@"D:\Workspace\_tmp\opencode", "transfer-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string data = Path.Combine(root, "flowdesk.json"), settings = Path.Combine(root, "app-settings.json");
            await File.WriteAllTextAsync(data, "original-data"); await File.WriteAllTextAsync(settings, "original-settings");
            string unrelated = Path.Combine(root, "keep.txt"); await File.WriteAllTextAsync(unrelated, "keep");
            string backups = Path.Combine(root, "backups"); Directory.CreateDirectory(Path.Combine(backups, "other-folder"));
            await File.WriteAllTextAsync(Path.Combine(backups, "other-folder", "keep.txt"), "keep");
            for (int i = 0; i < 7; i++) ImportBackup.Create(root, new DateTime(2026, 1, 1).AddSeconds(i));
            Assert.Equal(5, Directory.GetDirectories(backups, "before-import-*").Length);
            var saved = ImportBackup.Create(root, new DateTime(2026, 1, 2));
            Assert.True(File.Exists(Path.Combine(saved.Folder, "flowdesk.json"))); Assert.Equal(5, Directory.GetDirectories(backups, "before-import-*").Length);
            await File.WriteAllTextAsync(data, "new-data"); await File.WriteAllTextAsync(settings, "new-settings");
            await File.WriteAllTextAsync(Path.Combine(saved.Folder, "flowdesk.json"), "tampered");
            saved.Restore(); Assert.Equal("original-data", await File.ReadAllTextAsync(data)); Assert.Equal("original-settings", await File.ReadAllTextAsync(settings));
            Assert.Equal("keep", await File.ReadAllTextAsync(unrelated)); Assert.True(File.Exists(Path.Combine(backups, "other-folder", "keep.txt")));
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact] public void SameSecondBackupsHaveDistinctPathsAndMissingFilesUndoToAbsent()
    {
        string root = Path.Combine(@"D:\Workspace\_tmp\opencode", "transfer-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var a = ImportBackup.Create(root, new DateTime(2026, 1, 1)); var b = ImportBackup.Create(root, new DateTime(2026, 1, 1));
            Assert.NotEqual(a.Folder, b.Folder); File.WriteAllText(Path.Combine(root, "flowdesk.json"), "new"); a.Restore();
            Assert.False(File.Exists(Path.Combine(root, "flowdesk.json")));
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact] public async Task NativeShortcutConflictKeepsDefaultAndReportsOnlyRejectedEdit()
    {
        var settings = new AppSettings { ShortcutOverrides = new() { [HotkeyCatalog.PaletteId] = "Ctrl+Alt+P", ["snap-left"] = "Ctrl+Alt+J" } };
        IReadOnlyDictionary<string, string> applied = new Dictionary<string, string>();
        var warnings = await ImportedShortcuts.ApplyAsync(settings, bindings =>
        {
            if (bindings.ContainsKey(HotkeyCatalog.PaletteId)) throw new InvalidOperationException("Synthetic OS conflict");
            applied = bindings; return Task.CompletedTask;
        });
        Assert.Single(warnings); Assert.Contains(HotkeyCatalog.PaletteId, warnings[0]);
        Assert.Equal("Win+Alt+Space", settings.EffectiveShortcut(HotkeyCatalog.PaletteId).Shortcut);
        Assert.Equal("Ctrl+Alt+J", settings.ShortcutOverrides["snap-left"]); Assert.Equal(settings.ShortcutOverrides, applied);
    }
    [Fact] public async Task SuccessfulNativeSwapIsAppliedInOneTransaction()
    {
        var settings = new AppSettings { ShortcutOverrides = new() { ["snap-left"] = "Alt+D", ["snap-right"] = "Alt+A" } }; int calls = 0;
        var warnings = await ImportedShortcuts.ApplyAsync(settings, _ => { calls++; return Task.CompletedTask; });
        Assert.Empty(warnings); Assert.Equal(1, calls); Assert.Equal(2, settings.ShortcutOverrides.Count);
    }
    [Fact] public void EndpointCredentialsAreNotExportedEvenFromHandEditedSettings()
    {
        var settings = new AppSettings { Ai = new() { BaseUrl = "https://user:SECRET@example.org/v1?apiKey=SECRET" } };
        var json = SettingsTransfer.Export(new(), settings, new(), "test");
        Assert.DoesNotContain("SECRET", json); Assert.DoesNotContain("apiKey", json);
        Assert.Equal(new AiOptions().BaseUrl, JsonNode.Parse(json)!["sections"]!["settings"]!["Ai"]!["BaseUrl"]!.GetValue<string>());
    }
    [Fact] public async Task BoundedFileReaderReportsOversizeUnreadableAndMalformedFiles()
    {
        string root = Path.Combine(@"D:\Workspace\_tmp\opencode", "transfer-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            string path = Path.Combine(root, "large.flowdesk");
            using (var file = File.Create(path)) file.SetLength(SettingsTransfer.MaxBytes + 1);
            Assert.Contains("larger than 5 MB", (await SettingsTransfer.ReadAsync(path)).Error);
            Assert.Contains("could not be read", (await SettingsTransfer.ReadAsync(Path.Combine(root, "missing"))).Error);
            await File.WriteAllTextAsync(path, "{"); Assert.Contains("not valid", (await SettingsTransfer.ReadAsync(path)).Error);
        }
        finally { Directory.Delete(root, true); }
    }
    [Theory]
    [InlineData(@"\\server\share\program.exe")]
    [InlineData(@"\\?\UNC\server\share\program.exe")]
    [InlineData(@"\\?\D:\program.exe")]
    public void NetworkOrDeviceCommandTargetsAreNeverProbed(string target)
    {
        var settings = new AppSettings { CustomCommands = new() { Entries = [new("remote", "Remote", "remote", CustomTargetKind.Program, target)] } };
        var preview = Parse(SettingsTransfer.Export(new(), settings, new(), "test")); int probes = 0;
        var plan = SettingsTransfer.Merge(new(), new(), preview, new(), _ => { probes++; throw new InvalidOperationException("Must never probe"); });
        Assert.Equal(0, probes); Assert.Empty(plan.Settings.CustomCommands.Entries); Assert.Single(plan.Warnings);
        Assert.Contains("Network paths are not supported", plan.Warnings[0]);
    }
    [Fact] public void NullLocalAiOptionsDoNotPreventExport()
    {
        var json = SettingsTransfer.Export(new(), new() { Ai = null! }, new(), "test");
        var preview = Parse(json); Assert.Null(preview.Error); Assert.All(preview.Sections, row => Assert.Null(row.Error));
    }
    [Theory]
    [InlineData(AiProviderKind.OpenAICompatible, "https://attacker.example/v1")]
    [InlineData(AiProviderKind.Anthropic, "https://attacker.example/v1")]
    [InlineData(AiProviderKind.Anthropic, "https://local.example/v1")]
    public void AiImportKeepsLocalCredentialDestination(AiProviderKind provider, string url)
    {
        var local = new AppSettings { Ai = new() { Provider = AiProviderKind.OpenAICompatible, BaseUrl = "https://local.example/v1" } };
        var imported = new AppSettings { Ai = new() { Provider = provider, BaseUrl = url, Model = "imported-model", ShowFallback = false } };
        var plan = SettingsTransfer.Merge(new(), local, Parse(SettingsTransfer.Export(new(), imported, new(), "test")), new());
        Assert.Equal(local.Ai.Provider, plan.Settings.Ai.Provider); Assert.Equal(local.Ai.BaseUrl, plan.Settings.Ai.BaseUrl);
        Assert.Equal("imported-model", plan.Settings.Ai.Model); Assert.False(plan.Settings.Ai.ShowFallback);
        Assert.Contains("AI endpoint was not imported; set it in Settings if you want it", plan.Warnings);
    }
    [Theory]
    [InlineData(@"\\server\share\a")]
    [InlineData(@"\\?\UNC\server\share\a")]
    [InlineData(@"\\?\D:\a")]
    [InlineData("//server/share/a")]
    public void WorkspaceNetworkAndDevicePathsAreDroppedWithoutProbing(string path)
    {
        var data = new FlowDeskData { Workspaces = [new() { Name = "Imported", Items = [
            new() { Name = "App", App = new() { ExePath = path } },
            new() { Name = "File", Kind = ItemKind.File, Path = path },
            new() { Name = "Folder", Kind = ItemKind.Folder, Path = path },
            new() { Name = "Site", Kind = ItemKind.Website, Urls = ["https://example.org"] }] }] };
        int probes = 0;
        var plan = SettingsTransfer.Merge(new(), new(), Parse(SettingsTransfer.Export(data, new(), new(), "test")), new(), _ => { probes++; return true; });
        Assert.Equal(0, probes); Assert.Equal("Site", Assert.Single(plan.Data.Workspaces[0].Items).Name); Assert.Equal(3, plan.Warnings.Count);
    }
    [Fact] public void ReplaceOnlyReplacesCustomCommandsWhenTheirSectionIsSelected()
    {
        var local = new AppSettings { CustomCommands = new() { Entries = [new("old", "Old", "old", CustomTargetKind.Url, "https://example.org")] } };
        var incoming = new AppSettings { CustomCommands = new() { Entries = [new("new", "New", "new", CustomTargetKind.Url, "https://example.org")] } };
        var preview = Parse(SettingsTransfer.Export(new(), incoming, new(), "test"));
        var replaced = SettingsTransfer.Merge(new(), local, preview, new(Replace: true));
        var untouched = SettingsTransfer.Merge(new(), local, preview, new(CustomCommands: false, Replace: true));
        Assert.Equal("new", Assert.Single(replaced.Settings.CustomCommands.Entries).Id);
        Assert.Equal("old", Assert.Single(untouched.Settings.CustomCommands.Entries).Id);
    }
    [Fact] public async Task WorkerPreparationUsesDispatcherSnapshotNotLaterMutations()
    {
        var (data, settings) = Sample(); var preview = Parse(SettingsTransfer.Export(new(), new(), new(), "test"));
        var pending = SettingsTransfer.PrepareAsync(data, settings, preview, new(false, false, false, false));
        data.Workspaces.Clear(); settings.Theme = "Light";
        var plan = await pending;
        Assert.Single(plan.Data.Workspaces); Assert.Equal("Dark", plan.Settings.Theme);
    }
}
