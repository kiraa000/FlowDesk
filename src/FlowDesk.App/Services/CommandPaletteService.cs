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
    private CommandItem[] workspaces = [], actions = [];
    private CommandItem[] openWindows = [], systemCommands = [];
    private WebOptions web = new(true, true, WebEngine.Google, "");
    private volatile bool calculatorEnabled = true;
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
             new AppCommandSource(index.Apps), new SettingsCommandSource(PaletteShellIndex.LaunchAsync), new RecentFileCommandSource(index.Recent),
             new SnapshotCommandSource("Open windows", () => Volatile.Read(ref openWindows), PaletteMode.Windows),
             new SnapshotCommandSource("System commands", () => Volatile.Read(ref systemCommands)),
             new CalculatorCommandSource(() => calculatorEnabled, CopyAsync),
             new WebCommandSource(() => Volatile.Read(ref web), PaletteShellIndex.LaunchAsync)
        ], usage, (_, ex) => Log(ex));
        window = new(search, usage, this.notify);
        clipboard = new(window.Handle, () => window.RefreshResults());
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
        index.Apps.Changed += IndexChanged; index.Recent.Changed += IndexChanged;
    }
    private void IndexChanged() => dispatcher.TryEnqueue(() => { if (!disposed && window.IsOpen) window.RefreshResults(); });
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
        await services.Hotkeys.SetPaletteWindowsKeyAsync(services.Settings.PaletteWindowsKey);
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
        StartSourceSnapshots(mode);
        Snapshot(capturedTarget, capturedProcess);
        window.Open(target, services.Settings.Theme, started, mode);
    }
    private bool ModeEnabled(PaletteMode mode) => mode switch
    {
        PaletteMode.Windows => services.Settings.PaletteWindowsEnabled,
        PaletteMode.Calculator => services.Settings.PaletteCalculatorEnabled,
        PaletteMode.Web => services.Settings.PaletteWebEnabled,
        _ => true
    };
    private void SourcesChanged()
    {
        StartSourceSnapshots(window.IsOpen ? PaletteMode.Commands : PaletteMode.Clipboard);
        if (!window.IsOpen) return;
        if (!ModeEnabled(window.Mode)) window.SetMode(PaletteMode.Commands);
        else window.RefreshResults();
    }
    private void StartSourceSnapshots(PaletteMode mode)
    {
        var settings = services.Settings;
        calculatorEnabled = settings.PaletteCalculatorEnabled;
        Volatile.Write(ref web, new(settings.PaletteWebEnabled, settings.PaletteWebFallback, settings.PaletteWebEngine, settings.PaletteWebTemplate ?? ""));
        Volatile.Write(ref openWindows, []);
        Volatile.Write(ref systemCommands, []);
        long epoch = ++snapshotEpoch;
        if (mode == PaletteMode.Clipboard && !window.IsOpen) return;
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
            "Switch to " + w.Name, $"Workspace · {w.Model.Items.Count} apps", CommandKind.Workspace, "\uE8A1",
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
            Add("page-" + page, "Open FlowDesk · " + page, () => { EnsureIdle(); showMain(); vm.Navigate(page); return Task.CompletedTask; }, "\uE80F", "Open");
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
                            vm.ShowStatus($"Placed {placed} of {results.Count} windows.", placed == results.Count ? StatusKind.Success : StatusKind.Warning);
                        });
                        NotifyProblem();
                    }, "\uECA5", "Apply");
            if (PaletteActionRules.Applies("pause-shortcuts", state))
                Add("pause-shortcuts", services.Hotkeys.ShortcutRuntime.Paused ? "Resume window shortcuts" : "Pause window shortcuts",
                    () => { services.Hotkeys.TogglePause(); return Task.CompletedTask; }, "\uE769");
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
        vm.Settings.ApplyClipboardHistory = null; vm.Settings.ClearClipboardHistory = null; vm.Settings.PaletteSourcesChanged = null;
        clipboard.Dispose(); window.Shutdown(); index.Dispose();
    }
}
