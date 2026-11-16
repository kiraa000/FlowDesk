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
    private readonly PaletteShellIndex index = new();
    private readonly PaletteUsageStore usage;
    private readonly CommandPaletteWindow window;
    private CommandItem[] workspaces = [], actions = [];
    private nint capturedTarget;
    private uint capturedProcess;
    private bool disposed;
    public CommandPaletteService(AppServices services, AppViewModels vm, DispatcherQueue dispatcher, Action showMain, Func<Task> quit, Action<string> notify, Func<bool> available)
    {
        this.services = services; this.vm = vm; this.dispatcher = dispatcher; this.showMain = showMain; this.quit = quit;
        this.available = available;
        this.notify = notify;
        usage = new(Path.Combine(services.Store.Folder, "palette-usage.json"));
        var search = new CommandSearch([
            new WorkspaceCommandSource(() => Volatile.Read(ref workspaces)), new ActionCommandSource(() => Volatile.Read(ref actions)),
            new AppCommandSource(index.Apps), new SettingsCommandSource(PaletteShellIndex.LaunchAsync), new RecentFileCommandSource(index.Recent)
        ], usage, (_, ex) => Log(ex));
        window = new(search, usage, notify);
        window.BeforeQuery = () => Snapshot(capturedTarget, capturedProcess);
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
        });
    }
    public void Open() => Toggle(CommandPaletteWindow.Foreground, System.Diagnostics.Stopwatch.GetTimestamp(), onlyOpen: true);
    private void Toggle(nint target, long started, bool onlyOpen = false)
    {
        if (disposed || !available()) return;
        if (window.IsOpen) { if (!onlyOpen) window.HidePalette(); return; }
        capturedTarget = target; capturedProcess = CommandPaletteWindow.ProcessOf(target);
        Snapshot(capturedTarget, capturedProcess);
        window.Open(target, services.Settings.Theme, started);
    }
    private void Snapshot(nint target, uint targetProcess)
    {
        bool hasTarget = target != 0 && targetProcess != 0 && targetProcess != services.Windows.OwnProcessId && CommandPaletteWindow.ProcessOf(target) == targetProcess;
        // UI-owned VMs are read once, then only immutable arrays are searched on background threads.
        CommandItem[] workspaceItems = vm.IsBusy ? [] : vm.WorkspacesCollection.Select(w => new CommandItem("workspace:" + w.Model.Id,
            "Switch to " + w.Name, $"Workspace · {w.Model.Items.Count} apps", CommandKind.Workspace, "\uE8A1",
            async () => { EnsureIdle(); await vm.OpenAsync(w); NotifyProblem(); },
            () => { EnsureIdle(); showMain(); vm.Edit(w); return Task.CompletedTask; }, w.Name, "Switch")).ToArray();
        Volatile.Write(ref workspaces, workspaceItems);
        var list = new List<CommandItem>();
        var state = new PaletteActionState(vm.IsBusy, vm.Focus.IsRunning, vm.Focus.CanStart, services.Settings.WindowShortcutsEnabled, hasTarget);
        void Add(string id, string title, Func<Task> run, string glyph = "\uE8A7", string hint = "Run") =>
            list.Add(new("action:" + id, title, "FlowDesk action", CommandKind.Action, glyph, run, ActionHint: hint));
        foreach (var page in new[] { AppPage.Workspaces, AppPage.Layouts, AppPage.Focus, AppPage.Hotkeys, AppPage.Settings })
        {
            if (page == AppPage.Hotkeys && !vm.IsHotkeysVisible) continue;
            Add("page-" + page, "Open FlowDesk · " + page, () => { EnsureIdle(); showMain(); vm.Navigate(page); return Task.CompletedTask; }, "\uE80F", "Open");
        }
        if (!vm.IsBusy)
        {
            if (PaletteActionRules.Applies("start-focus", state))
                foreach (int duration in new[] { 25, 50, 90 })
                    Add("focus-" + duration, $"Start Focus {duration} min", async () =>
                    {
                        EnsureIdle(); if (!vm.Focus.CanStart) throw new InvalidOperationException();
                        vm.Focus.Duration = duration; await vm.Focus.StartCommand.ExecuteAsync(null);
                        if (!vm.Focus.IsRunning) notify("Focus could not start. Open the Focus page to check your session.");
                    }, "\uE916", "Start");
            if (PaletteActionRules.Applies("stop-focus", state))
                Add("focus-stop", "Stop Focus", async () => { EnsureIdle(); showMain(); await vm.Focus.EndSessionAsync(); }, "\uE71A", "Stop");
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
                                if (active is null || CommandPaletteWindow.ProcessOf(target) != targetProcess || active.ProcessId == services.Windows.OwnProcessId) throw new InvalidOperationException("The previous window is no longer available.");
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
        window.Shutdown(); index.Dispose();
    }
}
