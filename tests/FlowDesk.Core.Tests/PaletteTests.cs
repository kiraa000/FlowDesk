using FlowDesk.Core.Palette;
using FlowDesk.Core.Hotkeys;
using FlowDesk.Core.HotCorners;
using System.Text.Json;

namespace FlowDesk.Core.Tests;

public sealed class PaletteTests
{
    private static CommandItem Item(string id, string title = "Code", CommandKind kind = CommandKind.Action) =>
        new(id, title, kind.ToString(), kind, "", () => Task.CompletedTask);
    [Fact] public void MatcherTiersAndHighlightRanges()
    {
        var prefix = CommandMatcher.Match("Visual Studio", "vis");
        var word = CommandMatcher.Match("Open Visual Studio", "vis");
        var acronym = CommandMatcher.Match("Visual Studio", "vs");
        var substring = CommandMatcher.Match("Provision", "vis");
        var subsequence = CommandMatcher.Match("Violet intense", "vis");
        Assert.True(prefix.Score > word.Score && word.Score > acronym.Score && acronym.Score > substring.Score && substring.Score > subsequence.Score);
        Assert.Equal([new MatchRange(0, 3)], prefix.Highlights);
        Assert.Equal([new MatchRange(0, 1), new MatchRange(7, 1)], acronym.Highlights);
        Assert.Equal([new MatchRange(0, 2), new MatchRange(12, 1)], subsequence.Highlights);
        Assert.Equal(0, CommandMatcher.Match("Code", "zzz").Score);
    }
    [Theory]
    [InlineData("Café", "CAFE", 4)]
    [InlineData("Cafe\u0301", "café", 5)]
    public void CaseAndDiacriticsMapToOriginalText(string title, string query, int length)
    {
        var result = CommandMatcher.Match(title, query);
        Assert.True(result.Score >= 500); Assert.Equal([new MatchRange(0, length)], result.Highlights);
    }
    [Fact] public void SourceFailureIsolatesDeferredEnumeration()
    {
        int failures = 0;
        var engine = new CommandSearch([new BadSource(), new SnapshotCommandSource("ok", () => [Item("ok")])], Store(), (_, _) => failures++);
        Assert.Single(engine.Query("code")); Assert.Equal(1, failures);
    }
    private sealed class BadSource : ICommandSource
    {
        public string Name => "bad";
        public IEnumerable<CommandMatch> Query(string query) { yield return new(Item("bad"), 1, []); throw new IOException(); }
    }
    [Fact] public void UsageBoostCannotOverturnMatchingTierAndTiesAreStable()
    {
        var store = Store();
        var source = new SnapshotCommandSource("all", () => [Item("file", kind: CommandKind.File), Item("app", kind: CommandKind.App), Item("workspace", kind: CommandKind.Workspace)]);
        var engine = new CommandSearch([source], store, (_, _) => { });
        Assert.Equal(new[] { "workspace", "app", "file" }, engine.Query("code").Select(m => m.Item.Id));
        for (int i = 0; i < 500; i++) store.Record("file", DateTimeOffset.UtcNow);
        Assert.Equal("file", engine.Query("code")[0].Item.Id);
        Assert.InRange(store.Boost("file"), 1, 40);
        Assert.Equal("file", engine.Query("")[0].Item.Id);
        Assert.Single(engine.Query(""));
    }
    [Fact] public async Task UsageLoadSaveCorruptionAndCap()
    {
        string folder = Path.Combine(Path.GetTempPath(), "FlowDesk-palette-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder); string path = Path.Combine(folder, "usage.json");
        var store = new PaletteUsageStore(path, 2);
        store.Load(); Assert.Equal(0, store.Get("one").Count);
        var now = DateTimeOffset.UtcNow;
        store.Record("one", now); store.Record("two", now.AddSeconds(1)); store.Record("three", now.AddSeconds(2));
        await store.SaveAsync();
        var loaded = new PaletteUsageStore(path, 2); loaded.Load();
        Assert.Equal(0, loaded.Get("one").Count); Assert.Equal(1, loaded.Get("three").Count);
        await File.WriteAllTextAsync(path, "not JSON"); loaded.Load(); Assert.Equal(0, loaded.Get("three").Count);
        // Leave evidence in the task's configured D: temp area, no recursive deletion of paths.
    }
    [Fact] public async Task IndexPublishesAtomicallyCoalescesAndKeepsCacheAfterFailure()
    {
        var index = new CommandIndex(); int changed = 0;
        index.Changed += () => { Assert.NotEmpty(index.Snapshot); changed++; };
        await index.RefreshAsync(() => Task.FromResult<IReadOnlyList<CommandItem>>([Item("old")]));
        var pending = new TaskCompletionSource<IReadOnlyList<CommandItem>>();
        var refresh = index.RefreshAsync(() => pending.Task);
        Assert.Single(new AppCommandSource(index).Query("code"));
        bool duplicate = false;
        await index.RefreshAsync(() => { duplicate = true; return Task.FromResult<IReadOnlyList<CommandItem>>([]); });
        Assert.False(duplicate); Assert.Equal("old", index.Snapshot[0].Id);
        pending.SetResult([Item("new"), Item("new")]); await refresh; Assert.Single(index.Snapshot);
        await Assert.ThrowsAsync<IOException>(() => index.RefreshAsync(() => throw new IOException()));
        Assert.Equal("new", index.Snapshot[0].Id);
        Assert.Equal(2, changed);
    }
    [Fact] public void SeparateProvidersUseSnapshotsAndSynonyms()
    {
        Assert.Single(new WorkspaceCommandSource(() => [Item("workspace", "Switch to Study", CommandKind.Workspace)]).Query("study"));
        var actions = new ActionCommandSource(() => [Item("action", "Start Focus 25 min")]);
        Assert.Single(actions.Query("focus"));
        Assert.Contains(new SettingsCommandSource(_ => Task.CompletedTask).Query("wireless"), m => m.Item.Id == "settings:wifi");
        Assert.True(SettingsPageCatalog.All.Count >= 25);
        Assert.Equal(SettingsPageCatalog.All.Count, SettingsPageCatalog.All.Select(p => p.Id).Distinct().Count());
        Assert.All(SettingsPageCatalog.All, p => Assert.StartsWith("ms-settings:", p.Uri));
    }
    [Fact] public void RecentResolutionSkipsMissingAndBrokenLinksAndDeduplicates()
    {
        int errors = 0;
        var items = RecentFileCommandSource.Resolve(["good", "missing", "broken", "duplicate"],
            link => link == "broken" ? throw new IOException() : link == "missing" ? "gone.txt" : "notes.txt",
            target => target != "gone.txt", _ => Task.CompletedTask, _ => Task.CompletedTask, _ => errors++);
        Assert.Single(items); Assert.NotNull(items[0].Secondary); Assert.Equal(1, errors);
    }
    [Fact] public async Task RecentProviderQueriesItsResolvedSnapshotAndSearchCancels()
    {
        var index = new CommandIndex();
        await index.RefreshAsync(() => Task.FromResult<IReadOnlyList<CommandItem>>([Item("recent", "notes.txt", CommandKind.File)]));
        Assert.Single(new RecentFileCommandSource(index).Query("notes"));
        var engine = new CommandSearch([new RecentFileCommandSource(index)], Store(), (_, _) => { });
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        Assert.Throws<OperationCanceledException>(() => engine.Query("notes", cancel.Token));
    }
    [Fact] public void ActionsOnlyApplyInTheirValidState()
    {
        var state = new PaletteActionState(false, false, true, false, false);
        Assert.True(PaletteActionRules.Applies("start-focus", state)); Assert.False(PaletteActionRules.Applies("stop-focus", state));
        Assert.False(PaletteActionRules.Applies("pause-shortcuts", state)); Assert.False(PaletteActionRules.Applies("layout", state));
        state = state with { FocusRunning = true, ShortcutsEnabled = true, HasTarget = true };
        Assert.False(PaletteActionRules.Applies("start-focus", state)); Assert.True(PaletteActionRules.Applies("stop-focus", state));
        Assert.True(PaletteActionRules.Applies("pause-shortcuts", state)); Assert.True(PaletteActionRules.Applies("layout", state));
        Assert.False(PaletteActionRules.Applies("stop-focus", state with { Busy = true }));
    }
    [Theory]
    [InlineData(-2560, 0, 2560, 1440, 1.0)]
    [InlineData(2560, -100, 1920, 1080, 1.5)]
    [InlineData(0, 0, 600, 400, 2.0)]
    public void PlacementIsDpiCorrectAndStaysInsideWorkArea(int x, int y, int width, int height, double dpi)
    {
        var rect = PalettePlacement.Place(new(x, y, width, height), dpi);
        Assert.Equal(Math.Min(width, (int)Math.Round(640 * dpi)), rect.Width);
        Assert.Equal(x + (width - rect.Width) / 2, rect.X);
        Assert.InRange(rect.Y, y, y + height - rect.Height);
    }
    [Fact] public void PaletteBindingHasIndependentGroupAndStableCornerValues()
    {
        var binding = HotkeyCatalog.Find(HotkeyCatalog.PaletteId)!;
        Assert.Equal("Ctrl+Alt+Space", binding.Shortcut); Assert.Equal(3u, binding.Modifiers); Assert.False(binding.Pausable);
        Assert.Equal(ShortcutGroup.CommandPalette, ShortcutPresentation.Group(binding));
        Assert.Contains(ShortcutPresentation.Groups, g => g.Title == "Command palette");
        Assert.Equal(4, (int)HotCornerAction.Lock); Assert.Equal(5, (int)HotCornerAction.CommandPalette);
        Assert.Equal(HotCornerAction.Lock, JsonSerializer.Deserialize<HotCornerAction>("4"));
        Assert.Equal(HotCornerAction.CommandPalette, JsonSerializer.Deserialize<HotCornerAction>(JsonSerializer.Serialize(HotCornerAction.CommandPalette)));
    }
    [Fact] public void PaletteRegistersIndependentlyAndPreferencesDoNotEnableOtherModules()
    {
        var policy = new ShortcutPolicy([]);
        var paletteOnly = policy.ForModules(false, true);
        Assert.True(paletteOnly.CanRegister(HotkeyCatalog.Find(HotkeyCatalog.PaletteId)!, false));
        Assert.All(HotkeyCatalog.All.Where(b => b.Id != HotkeyCatalog.PaletteId), b => Assert.False(paletteOnly.Enabled(b.Id)));
        Assert.False(policy.ForModules(true, false).Enabled(HotkeyCatalog.PaletteId));
        Assert.False(new ShortcutPolicy([HotkeyCatalog.PaletteId]).ForModules(false, true).Enabled(HotkeyCatalog.PaletteId));
    }
    private static PaletteUsageStore Store() => new(Path.Combine(Path.GetTempPath(), "unused-palette-usage.json"));
    [Theory]
    [InlineData("Shell_TrayWnd")]
    [InlineData("Shell_SecondaryTrayWnd")]
    [InlineData("Progman")]
    [InlineData("WorkerW")]
    [InlineData("MultitaskingViewFrame")]
    [InlineData("")]
    public void LayoutTargetsRejectShellSurfacesAndOwnOrMissingProcess(string shellClass)
    {
        Assert.False(PaletteTargetRules.IsAppTarget(10, 20, shellClass));
        Assert.False(PaletteTargetRules.IsAppTarget(20, 20, "AppWindow"));
        Assert.False(PaletteTargetRules.IsAppTarget(0, 20, "AppWindow"));
        Assert.True(PaletteTargetRules.IsAppTarget(10, 20, "ApplicationFrameWindow"));
        Assert.True(PaletteTargetRules.IsAppTarget(10, 20, "CabinetWClass"));
    }
    [Theory]
    [InlineData(true, true, false, false, 0, PaletteFocusDecision.Retry)]
    [InlineData(true, true, false, true, 0, PaletteFocusDecision.Focused)]
    [InlineData(false, true, false, true, 0, PaletteFocusDecision.Cancel)]
    [InlineData(true, false, false, false, 0, PaletteFocusDecision.Cancel)]
    [InlineData(true, false, true, false, 3, PaletteFocusDecision.Failed)]
    [InlineData(true, true, false, false, 3, PaletteFocusDecision.Failed)]
    public void FocusRetryRequiresBothForegroundAndSearchAndRespectsUserOrEpochChange(bool current, bool palette, bool original, bool focused, int attempt, PaletteFocusDecision expected)
        => Assert.Equal(expected, PaletteFocusRules.Decide(current, palette, original, focused, attempt));
}
