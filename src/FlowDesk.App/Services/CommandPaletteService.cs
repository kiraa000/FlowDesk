using FlowDesk.App.ViewModels;
using FlowDesk.App.Views;
using FlowDesk.Core.Palette;
using Microsoft.UI.Dispatching;

namespace FlowDesk.App.Services;

internal sealed class CommandPaletteService : IDisposable
{
    private readonly AppServices services;
    private readonly AppViewModels vm;
    private readonly Action showMain;
    private readonly Func<Task> quit;
    private readonly DispatcherQueue dispatcher;
    private readonly Func<bool> available;
    private readonly Action<string> notify;
    private readonly Action<PaletteNotice> notifyNotice;
    private readonly PaletteShellIndex index = new();
    private readonly PaletteUsageStore usage;
    private readonly CommandPaletteWindow window;
    private readonly ClipboardHistoryService clipboard;
    private readonly PaletteWindowSwitcher switcher;
    private readonly IPaletteSystem system;
    private readonly PaletteFileSource files;
    private long fileDetectionEpoch;
    private CommandItem[] workspaces = [], actions = [];
    private CommandItem[] openWindows = [], systemCommands = [];
    private WebOptions web = new(true, true, WebEngine.Google, "");
    private CustomCommandSettings customCommands = new();
    private readonly CommandIndex bookmarks = new();
    private readonly PaletteBookmarkReader bookmarkReader = new();
    private BookmarkOptions bookmarkOptions = new();
    private readonly IAiCredentialStore aiCredentials = new AiCredentialStore();
    private AiAvailability ai = new(new(), false);
    private volatile bool calculatorEnabled = true;
    private volatile bool fileSearchEnabled = true;
    private long snapshotEpoch;
    private Task<CommandItem[]>? windowsRead, systemRead;
    private nint capturedTarget;
    private uint capturedProcess;
    private bool disposed;
    public CommandPaletteService(AppServices services, AppViewModels vm, DispatcherQueue dispatcher, Action showMain, Func<Task> quit, Action<PaletteNotice> notify, Func<bool> available)
    {
        this.services = services; this.vm = vm; this.dispatcher = dispatcher; this.showMain = showMain; this.quit = quit;
        this.available = available;
        notifyNotice = notify;
        this.notify = message => notify(new(message));
        usage = new(Path.Combine(services.Store.Folder, "palette-usage.json"));
        var search = new CommandSearch([
            new WorkspaceCommandSource(() => Volatile.Read(ref workspaces)), new ActionCommandSource(() => Volatile.Read(ref actions)),
              new AppCommandSource(index.Apps), new SettingsCommandSource(PaletteShellIndex.LaunchAsync),
              new SnapshotCommandSource("Recent files", () => fileSearchEnabled ? index.Recent.Snapshot : []),
              new CustomCommandSource(() => Volatile.Read(ref customCommands), CustomCommandExecutor.RunAsync),
              new RunCommandSource(usage, CustomCommandExecutor.RunAsync, usage.SaveAsync, RefreshWindowResults),
              new SnapshotCommandSource("Bookmarks", BookmarkSnapshot, PaletteMode.Bookmarks),
              new AiCommandSource(() => Volatile.Read(ref ai), AskAiAsync),
             new SnapshotCommandSource("Open windows", () => Volatile.Read(ref openWindows), PaletteMode.Windows),
             new SnapshotCommandSource("System commands", () => Volatile.Read(ref systemCommands)),
             new CalculatorCommandSource(() => calculatorEnabled, CopyAsync),
             new WebCommandSource(() => Volatile.Read(ref web), PaletteShellIndex.LaunchAsync)
        ], usage, (_, ex) => Log(ex));
        var slowSources = new List<IAsyncCommandSource>();
        window = new(new PaletteStreamingSearch(search, slowSources, (_, ex) => Log(ex)), usage, this.notify);
        clipboard = new(window.Handle, () => window.RefreshResults());
        files = new(usage, CopyAsync, path => clipboard.CopyFile(path), () =>
        { showMain(); vm.OpenAdvancedFromCalm(); return Task.CompletedTask; }, () => window.Handle, PreviewPathAsync);
        window.ClosePreview = () => services.QuickLook?.ClosePathPreview(window.Handle) == true;
        window.CopyAnswerText = CopyAsync;
        window.AskAgain = AskAiAsync;
        window.OpenAiChat = () => PaletteShellIndex.LaunchAsync(AiRules.ChatUrl(Volatile.Read(ref ai).Options));
        slowSources.Add(files);
        index.FileItem = files.Item;
        switcher = new(services, CopyAsync, this.notify, window.HidePalette);
        system = new PaletteWindowsSystem(services);
        index.CopyPath = CopyAsync;
        window.ModeEnabled = ModeEnabled;
        window.ClipboardEnabled = () => clipboard.History.Enabled;
        window.ClipboardQuery = QueryClipboard;
        window.ClearClipboard = clipboard.Clear;
        vm.Settings.ApplyClipboardHistory = SetClipboardAsync;
        vm.Settings.ClearClipboardHistory = clipboard.Clear;
        vm.Settings.PaletteSourcesChanged = SourcesChanged;
        vm.Settings.ClearRunHistory = async () =>
        {
            usage.ClearRunHistory();
            try { await Task.Run(usage.SaveAsync); window.RefreshResults(); }
            catch (Exception) { this.notify("Run history could not be saved. Try again."); }
        };
        vm.Settings.AiSettingsChanged = RefreshAiSettings;
        RefreshAiSettings();
        index.Apps.Changed += IndexChanged; index.Recent.Changed += IndexChanged;
        bookmarks.Changed += IndexChanged;
    }
    private void IndexChanged() => dispatcher.TryEnqueue(() => { if (!disposed && window.IsOpen) window.RefreshResults(); });
    private void RefreshWindowResults() => dispatcher.TryEnqueue(() => { if (!disposed) window.RefreshResults(); });
    private Task PreviewPathAsync(string path)
    {
        if (services.QuickLook?.Enabled != true) notify("Turn on Quick Look in Settings to preview files.");
        else if (!services.QuickLook.ShowPath(path, window.Handle)) notify("That file could not be previewed. Try again.");
        return Task.CompletedTask;
    }
    private Task AskAiAsync(string question)
    {
        var configuration = Volatile.Read(ref ai);
        return window.ShowAnswerAsync(new PaletteAiProvider(configuration.Options, aiCredentials), question);
    }
    private void RefreshAiSettings()
    {
        var options = services.Settings.Ai ?? new(); bool saved;
        try { saved = !string.IsNullOrEmpty(aiCredentials.Read(options.Provider)); }
        catch (Exception) { saved = false; }
        Volatile.Write(ref ai, new(options, saved));
        if (window.IsOpen) window.RefreshResults();
    }
    public async Task InitializeAsync()
    {
        await Task.Run(usage.Load);
        await services.Hotkeys.SetPaletteAsync(() =>
        {
            // Capture the foreground before dispatch, not after the palette has activated.
            nint target = CommandPaletteWindow.Foreground;
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            dispatcher.TryEnqueue(() => Toggle(target, started));
        }, () =>
        {
            nint target = CommandPaletteWindow.Foreground;
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            dispatcher.TryEnqueue(() => Toggle(target, started, mode: PaletteMode.Clipboard));
        });
        try { await services.Hotkeys.SetPaletteWindowsKeyAsync(services.Settings.PaletteWindowsKey); }
        catch (Exception ex) { Log(ex); notify("Windows-key option unavailable. The regular palette shortcut still works."); }
        if (services.Settings.ClipboardHistoryEnabled)
        {
            try { await SetClipboardAsync(true); }
            catch { services.Settings.ClipboardHistoryEnabled = false; notify("Clipboard history could not start. Turn it on in Settings to retry."); }
        }
    }
    private async Task SetClipboardAsync(bool enabled)
    {
        if (disposed) return;
        if (!enabled)
        {
            // Privacy takes precedence over registration failures: off clears immediately and is never rolled back.
            clipboard.SetEnabled(false); services.Settings.ClipboardHistoryEnabled = false;
            window.SetMode(PaletteMode.Commands);
            services.Changed();
            await services.Hotkeys.SetDisabledShortcutsAsync(services.Settings.DisabledShortcuts, false);
            return;
        }
        clipboard.SetEnabled(true);
        try
        {
            await services.Hotkeys.SetDisabledShortcutsAsync(services.Settings.DisabledShortcuts, true);
            services.Settings.ClipboardHistoryEnabled = true;
        }
        catch { clipboard.SetEnabled(false); services.Settings.ClipboardHistoryEnabled = false; throw; }
    }
    private Task<IReadOnlyList<CommandMatch>> QueryClipboard(string query, CancellationToken token)
    {
        // Snapshot on the UI thread; matching long copied text never blocks typing or capture.
        var entries = clipboard.History.Enabled ? clipboard.History.Snapshot() : [];
        return Task.Run<IReadOnlyList<CommandMatch>>(() =>
        {
            var matches = new List<CommandMatch>();
            foreach (var e in entries)
            {
                token.ThrowIfCancellationRequested();
                if (!ClipboardHistory.Matches(e, query)) continue;
                string title = e.Display.Length > 512 ? e.Display[..512] + "…" : e.Display;
                var item = new CommandItem("clipboard:" + e.Id, title, e.Subtitle(DateTimeOffset.UtcNow), CommandKind.Clipboard, "\uE8A5",
                    () => UseClipboardAsync(e, paste: true), () => UseClipboardAsync(e, paste: false), ActionHint: "Paste", RecordUsage: false,
                    NamedActions: [new("Paste", () => UseClipboardAsync(e, true), "\uE77F", "Enter"),
                        new("Copy", () => UseClipboardAsync(e, false), "\uE8C8", "Ctrl+Enter"),
                        new("Delete from history", () => { clipboard.History.Delete(e.Id); window.RefreshResults(); return Task.CompletedTask; }, "\uE74D", KeepsPaletteOpen: true)]);
                matches.Add(new(item, 1, CommandMatcher.Match(item.Title, query, allowFuzzy: false).Highlights));
            }
            return matches;
        }, token);
    }
    private Task CopyAsync(string text)
    {
        try { clipboard.Copy(text); notifyNotice(PaletteNotice.Copied(pasteFallback: false)); }
        catch { notify("Clipboard is busy. Try again."); }
        return Task.CompletedTask;
    }
    private async Task UseClipboardAsync(ClipboardEntry entry, bool paste)
    {
        if (disposed || !clipboard.History.Enabled || !clipboard.History.Snapshot().Any(e => e.Id == entry.Id)) return;
        nint target = capturedTarget; uint process = capturedProcess;
        try { clipboard.Copy(entry.Text); }
        catch { notify("Clipboard is busy. Try again."); return; }
        if (!paste) { notifyNotice(PaletteNotice.Copied(pasteFallback: false)); return; }
        if (!await ClipboardPaste.TryPasteAsync(target, process, window.Handle)) notifyNotice(PaletteNotice.Copied(pasteFallback: true));
    }
    public void Open() => Toggle(CommandPaletteWindow.Foreground, System.Diagnostics.Stopwatch.GetTimestamp(), onlyOpen: true);
    private void Toggle(nint target, long started, bool onlyOpen = false, PaletteMode mode = PaletteMode.Commands)
    {
        if (disposed || !available()) return;
        if (mode == PaletteMode.Clipboard && !clipboard.History.Enabled) return;
        if (window.IsOpen)
        {
            if (mode == PaletteMode.Clipboard && window.Mode != mode) window.SetMode(mode);
            else if (!onlyOpen) window.HidePalette();
            return;
        }
        capturedTarget = target; capturedProcess = CommandPaletteWindow.ProcessOf(target);
        StartFileDetection();
        StartSourceSnapshots(mode);
        Snapshot(capturedTarget, capturedProcess);
        window.Open(target, services.Settings.Theme, started, mode);
    }
    private bool ModeEnabled(PaletteMode mode) => mode switch
    {
        PaletteMode.Windows => services.Settings.PaletteWindowsEnabled,
        PaletteMode.Calculator => services.Settings.PaletteCalculatorEnabled,
        PaletteMode.Web => services.Settings.PaletteWebEnabled,
        PaletteMode.Files => services.Settings.PaletteFilesEnabled,
        PaletteMode.Bookmarks => services.Settings.PaletteEdgeBookmarks || services.Settings.PaletteChromeBookmarks || services.Settings.PaletteFirefoxBookmarks,
        PaletteMode.Ai => Volatile.Read(ref ai).Configured,
        _ => true
    };
    private void SourcesChanged()
    {
        StartFileDetection();
        StartSourceSnapshots(window.IsOpen ? PaletteMode.Commands : PaletteMode.Clipboard);
        if (!window.IsOpen) return;
        if (!ModeEnabled(window.Mode)) window.SetMode(PaletteMode.Commands);
        else window.RefreshResults();
    }
    private void StartFileDetection()
    {
        long epoch = ++fileDetectionEpoch;
        bool enabled = services.Settings.PaletteFilesEnabled;
        fileSearchEnabled = enabled;
        var detected = files.BeginOpen(enabled);
        vm.Settings.PaletteFileBackendStatus = enabled ? "Checking file search…" : "Off";
        _ = PublishFileStatusAsync(detected, epoch, enabled);
    }
    private async Task PublishFileStatusAsync(Task<IPaletteFileBackend?> detected, long epoch, bool enabled)
    {
        IPaletteFileBackend? backend;
        try { backend = await detected.ConfigureAwait(false); }
        catch (Exception) { backend = null; }
        dispatcher.TryEnqueue(() =>
        {
            if (!disposed && epoch == fileDetectionEpoch)
                vm.Settings.PaletteFileBackendStatus = !enabled ? "Off" : backend?.Name ?? "File search needs Everything or Windows Search";
        });
    }
    private void StartSourceSnapshots(PaletteMode mode)
    {
        var settings = services.Settings;
        var options = new BookmarkOptions(settings.PaletteEdgeBookmarks, settings.PaletteChromeBookmarks, settings.PaletteFirefoxBookmarks);
        Volatile.Write(ref bookmarkOptions, options);
        calculatorEnabled = settings.PaletteCalculatorEnabled;
        var custom = settings.CustomCommands ?? new();
        Volatile.Write(ref customCommands, new() { Version = custom.Version, Entries = [.. (custom.Entries ?? []).Where(c => c is not null)] });
        Volatile.Write(ref web, new(settings.PaletteWebEnabled, settings.PaletteWebFallback, settings.PaletteWebEngine, settings.PaletteWebTemplate ?? ""));
        Volatile.Write(ref openWindows, []);
        Volatile.Write(ref systemCommands, []);
        long epoch = ++snapshotEpoch;
        if (mode == PaletteMode.Clipboard && !window.IsOpen) return;
        _ = RefreshBookmarksAsync(options);
        if (settings.PaletteWindowsEnabled)
        {
            if (windowsRead is not { IsCompleted: false }) windowsRead = Task.Run(() => switcher.Snapshot(index.Apps.Snapshot));
            _ = PublishAsync(windowsRead, isWindows: true, epoch);
        }
        if (settings.PaletteSystemEnabled)
        {
            if (systemRead is not { IsCompleted: false }) systemRead = Task.Run(async () => SystemCommands.Create(system, await system.ReadAsync()).ToArray());
            _ = PublishAsync(systemRead, isWindows: false, epoch);
        }
    }
    private IReadOnlyList<CommandItem> BookmarkSnapshot()
    {
        var options = Volatile.Read(ref bookmarkOptions);
        return bookmarks.Snapshot.Where(item => Enum.GetValues<BookmarkBrowser>().Any(browser => options.Enabled(browser) && item.Id.StartsWith("bookmark:" + browser + ":", StringComparison.Ordinal))).ToArray();
    }
    private async Task RefreshBookmarksAsync(BookmarkOptions options)
    {
        try { await bookmarks.RefreshAsync(() => Task.Run<IReadOnlyList<CommandItem>>(() => bookmarkReader.Read(options).Select(BookmarkItem).ToArray())); }
        catch (Exception) { /* An inaccessible browser profile must not erase the last usable snapshot. */ }
    }
    private CommandItem BookmarkItem(BookmarkEntry bookmark)
    {
        Func<Task> open = () => CustomCommandExecutor.RunAsync(new(bookmark.Profile.Executable,
            bookmark.Profile.Arguments + " " + CustomCommandRules.Quote(bookmark.Url), false));
        return new(bookmark.Id, bookmark.Title, bookmark.Subtitle, CommandKind.Bookmark, bookmark.Profile.Executable, open,
            Synonyms: bookmark.Url, NamedActions: [new("Open in " + bookmark.Profile.Browser + " · " + bookmark.Profile.Name, open, "\uE774", "Enter"),
                new("Open in default browser", () => PaletteShellIndex.LaunchAsync(bookmark.Url), "\uE774", "Ctrl+Enter"),
                new("Copy URL", () => CopyAsync(bookmark.Url), "\uE8C8")]);
    }
    private async Task PublishAsync(Task<CommandItem[]> load, bool isWindows, long epoch)
    {
        try
        {
            var items = await load.ConfigureAwait(false);
            dispatcher.TryEnqueue(() =>
            {
                if (disposed || !window.IsOpen || epoch != snapshotEpoch) return;
                if (isWindows) Volatile.Write(ref openWindows, items);
                else Volatile.Write(ref systemCommands, items);
                if (window.Mode == PaletteMode.Commands || (isWindows && window.Mode == PaletteMode.Windows)) window.RefreshResults();
            });
        }
        catch (Exception ex) { Log(ex); }
    }
    private void Snapshot(nint target, uint targetProcess)
    {
        bool hasTarget = PaletteTargetRules.IsAppTarget(targetProcess, (uint)services.Windows.OwnProcessId, CommandPaletteWindow.ClassOf(target)) && CommandPaletteWindow.ProcessOf(target) == targetProcess;
        // UI-owned VMs are read once, then only immutable arrays are searched on background threads.
        CommandItem[] workspaceItems = vm.IsBusy ? [] : vm.WorkspacesCollection.Select(w => new CommandItem("workspace:" + w.Model.Id,
            "Switch to " + w.Name, $"Workspace · {FlowDesk.Core.CountText.Format(w.Model.Items.Count, "item")}", CommandKind.Workspace, "\uE8A1",
            async () => { EnsureIdle(); await vm.OpenAsync(w); NotifyProblem(); },
            () => { EnsureIdle(); showMain(); vm.Edit(w); return Task.CompletedTask; }, w.Name)).ToArray();
        Volatile.Write(ref workspaces, workspaceItems);
        var list = new List<CommandItem>();
        if (clipboard.History.Enabled)
            list.Add(new("action:clipboard-history", "Clipboard history", "Search text copied during this session", CommandKind.Action, "\uE8A5",
                () => { window.SetMode(PaletteMode.Clipboard); return Task.CompletedTask; }, ActionHint: "Open", KeepsPaletteOpen: true));
        var state = new PaletteActionState(vm.IsBusy, vm.Focus.IsRunning, vm.Focus.CanStart, services.Settings.WindowShortcutsEnabled, hasTarget);
        void Add(string id, string title, Func<Task> run, string glyph = "\uE8A7", string hint = "Run") =>
            list.Add(new("action:" + id, title, "FlowDesk action", CommandKind.Action, glyph, run, ActionHint: hint));
        foreach (var page in new[] { AppPage.Workspaces, AppPage.Layouts, AppPage.Focus, AppPage.Calm, AppPage.Hotkeys, AppPage.Advanced, AppPage.Settings })
        {
            if (page == AppPage.Hotkeys && !vm.IsHotkeysVisible) continue;
            if (page == AppPage.Advanced && !vm.IsAdvancedVisible) continue;
            Add("page-" + page, "Open FlowDesk · " + (page == AppPage.Calm ? "Calm Windows" : page.ToString()),
                () => { EnsureIdle(); showMain(); vm.Navigate(page); return Task.CompletedTask; }, "\uE80F", "Open");
        }
        foreach (var tool in FlowDesk.Core.Advanced.AdvancedTools.PaletteTools(vm.IsAdvancedVisible,
            vm.Advanced.Tools.Where(t => t.Installed).Select(t => t.Tool.Id)))
        {
            var card = vm.Advanced.Tools.Single(t => t.Tool.Id == tool.Id);
            string title = tool.Id == "Microsoft.PowerToys" ? "PowerToys" : tool.Name;
            Add("advanced-" + tool.Id, "Open " + title, async () =>
            {
                EnsureIdle();
                if (!vm.IsAdvancedVisible) return; // Snapshot may outlive the setting.
                await vm.Advanced.OpenAsync(card);
                if (card.Message is { } message) notify(message);
            }, "\uE90F", "Open");
        }
        if (!vm.IsBusy)
        {
            Add("calm-check", "Run check-up", async () => { EnsureIdle(); showMain(); vm.Navigate(AppPage.Calm); await vm.Calm.RunCheckCommand.ExecuteAsync(null); }, "\uE8BE");
            Task OpenFocusSetup()
            {
                EnsureIdle(); showMain(); vm.Navigate(AppPage.Focus); return Task.CompletedTask;
            }
            list.AddRange(PaletteFocusCommands.Create(state, async duration =>
            {
                EnsureIdle();
                if (!vm.Focus.CanStart) { await OpenFocusSetup(); return; }
                vm.Focus.Duration = duration; await vm.Focus.StartCommand.ExecuteAsync(null);
                if (!vm.Focus.IsRunning) notify("Focus could not start. Open the Focus page to check your session.");
            }, OpenFocusSetup, async () => { EnsureIdle(); showMain(); await vm.Focus.EndSessionAsync(); }));
            Add("save-workspace", "Save open windows as workspace", async () =>
            { EnsureIdle(); showMain(); await vm.Workspaces.NewFromWindowsCommand.ExecuteAsync(null); }, "\uE74E", "Save");
            Add("export-settings", "Export FlowDesk settings", async () =>
            {
                EnsureIdle();
                if (vm.Settings.ExportTransfer is not { } export) return;
                showMain();
                await export();
            }, "\uE896", "Export");
            Add("import-settings", "Import FlowDesk settings", async () =>
            {
                EnsureIdle();
                if (vm.Settings.ImportTransfer is not { } import) return;
                showMain();
                await import();
            }, "\uE8E5", "Import");
            if (PaletteActionRules.Applies("layout", state))
                foreach (var layout in vm.Layouts.Presets)
                    Add("layout-" + layout.Preset, "Apply layout " + layout.Name, async () =>
                    {
                        EnsureIdle();
                        await vm.ExecuteAsync(async () =>
                        {
                            var results = await Task.Run(() =>
                            {
                                var active = services.Windows.Get((long)target);
                                if (active is null || CommandPaletteWindow.ProcessOf(target) != targetProcess ||
                                    !PaletteTargetRules.IsAppTarget(targetProcess, (uint)services.Windows.OwnProcessId, active.ClassName))
                                    throw new InvalidOperationException("The previous window is no longer available.");
                                return services.Layouts.Apply(layout.Preset, [active], active.MonitorDevice);
                            });
                            int placed = results.Count(r => r.Outcome == FlowDesk.Core.Windows.PlaceOutcome.Ok);
                            vm.ShowStatus($"Placed {placed} of {FlowDesk.Core.CountText.Format(results.Count, "window") }.", placed == results.Count ? StatusKind.Success : StatusKind.Warning);
                        });
                        NotifyProblem();
                    }, "\uECA5", "Apply");
            if (PaletteActionRules.Applies("pause-shortcuts", state))
                Add("pause-shortcuts", services.Hotkeys.ShortcutRuntime.Paused ? "Resume window shortcuts" : "Pause window shortcuts",
                    () => { services.Hotkeys.TogglePause(); return Task.CompletedTask; }, "\uE769");
            Add("game-mode", services.Hotkeys.GameMode ? "Turn off game mode" : "Turn on game mode",
                async () => { EnsureIdle(); await services.Hotkeys.ToggleGameModeAsync(); }, "\uE7FC");
            Add("quit", "Quit FlowDesk", async () => { EnsureIdle(); showMain(); await quit(); }, "\uE7E8", "Quit");
        }
        Volatile.Write(ref actions, list.ToArray());
    }
    private void EnsureIdle() { if (disposed || !available() || vm.IsBusy) throw new InvalidOperationException("FlowDesk is busy. Try again when the current operation finishes."); }
    private void NotifyProblem()
    {
        // Existing workspace/layout commands report failures inline rather than throwing. Surface those
        // through the same notification path when the palette ran them with the main window hidden.
        if (vm.StatusKind is StatusKind.Warning or StatusKind.Error) notify(vm.StatusMessage);
    }
    private static void Log(Exception ex) => new CrashLog().TryWrite(CrashSource.Activation, ex);
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        index.Apps.Changed -= IndexChanged; index.Recent.Changed -= IndexChanged;
        bookmarks.Changed -= IndexChanged;
        vm.Settings.ApplyClipboardHistory = null; vm.Settings.ClearClipboardHistory = null; vm.Settings.PaletteSourcesChanged = null;
        vm.Settings.ClearRunHistory = null;
        vm.Settings.AiSettingsChanged = null;
        clipboard.Dispose(); window.Shutdown(); index.Dispose();
    }
}
