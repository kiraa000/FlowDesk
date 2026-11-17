using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlowDesk.App.Services;
using FlowDesk.Core.Calm;

namespace FlowDesk.App.ViewModels;

public sealed partial class CalmViewModel : ObservableObject
{
    private readonly AppViewModels parent;
    public string AdvancedLinkText => parent.IsAdvancedVisible ? "Open Advanced" : "Turn on in Settings";
    internal void RefreshAdvancedLink() => OnPropertyChanged(nameof(AdvancedLinkText));
    [RelayCommand] private void OpenAdvanced() => parent.OpenAdvancedFromCalm();
    private readonly IRegistryAccess registry = new WindowsRegistryAccess();
    private readonly IStartupFolder folder = new WindowsStartupFolder();
    private readonly CalmChanges changes;
    private readonly CalmScanSession scanSession = new();
    private IReadOnlyList<CalmReading> readings = [];
    private IReadOnlyList<StartupEntry> startup = [];
    public ObservableCollection<CalmGroup> Groups { get; } = [];
    public ObservableCollection<CalmStartupRow> StartupApps { get; } = [];
    [ObservableProperty] public partial bool Busy { get; set; }
    [ObservableProperty] public partial bool HasScan { get; set; }
    [ObservableProperty] public partial string Summary { get; set; } = "A quieter Windows, on your terms";
    [ObservableProperty] public partial string LastChecked { get; set; } = "Not checked yet. Nothing changes until you choose it.";
    [ObservableProperty] public partial string Error { get; set; } = "";
    [ObservableProperty] public partial bool ErrorOpen { get; set; }
    [ObservableProperty] public partial bool RestartSuggested { get; set; }
    [ObservableProperty] public partial string ActiveHours { get; set; } = "Managed in Windows Settings.";
    [ObservableProperty] public partial bool OfferOpen { get; set; }
    [ObservableProperty] public partial string OfferText { get; set; } = "";
    [ObservableProperty] public partial bool HasUndo { get; set; }
    public bool CanInteract => !Busy;
    partial void OnBusyChanged(bool value) => OnPropertyChanged(nameof(CanInteract));
    public CalmViewModel(AppViewModels parent)
    {
        this.parent = parent;
        changes = new(registry, new FileCalmJournalStore(parent.Services.Store.Folder));
    }
    internal async Task FirstCheckAsync()
    {
        try { HasUndo = await Task.Run(() => changes.HasAnyUndo); }
        catch { Error = "The undo journal could not be read. Windows changes are disabled until the journal is recovered."; ErrorOpen = true; return; }
        if (parent.Services.Settings.FirstCalmCheckDone) return;
        await EnsureScanForVisitAsync();
        if (!HasScan) return;
        parent.Services.Settings.FirstCalmCheckDone = true;
        parent.Services.Changed();
        int count = readings.Count(r => r.IsFinding);
        OfferText = $"Check-up found {count} {(count == 1 ? "thing" : "things")} you can calm down.";
        OfferOpen = count > 0;
    }
    [RelayCommand] private void Review() { OfferOpen = false; parent.Navigate(AppPage.Calm); }
    internal Task EnsureScanForVisitAsync() => scanSession.EnsureAsync(() => HasScan, RunCheckAsync);
    [RelayCommand(AllowConcurrentExecutions = true)] private async Task RunCheckAsync() => await OperateAsync(async () =>
    {
        var result = await Task.Run(() => (Rows: CalmEvaluator.Scan(registry, CalmWindows.Build(registry)), Startup: StartupCatalog.Scan(registry, folder), Hours: CalmWindows.ActiveHours(registry)));
        readings = result.Rows; startup = result.Startup; ActiveHours = result.Hours;
        RefreshRows();
        HasScan = true; LastChecked = "Last checked " + DateTimeOffset.Now.ToString("g");
    });
    private void RefreshRows()
    {
        CalmRowRefresh.Apply(Groups, StartupApps, readings, startup, changes.HasUndo,
            ChangeAsync, StartupAsync, () => CanInteract);
        HasUndo = changes.HasAnyUndo;
        RestartSuggested |= readings.Any(r => r.Setting.ExplorerRestart && changes.HasUndo(r.Setting.Id));
        parent.Settings.RefreshStartupState();
        int count = readings.Count(r => r.IsFinding);
        Summary = count == 0 ? "No suggested changes found" : $"{count} {(count == 1 ? "thing" : "things")} you can calm down";
    }
    private async Task ReloadAfterChangeAsync()
    {
        var result = await Task.Run(() => (Rows: CalmEvaluator.Scan(registry, CalmWindows.Build(registry)), Startup: StartupCatalog.Scan(registry, folder)));
        readings = result.Rows; startup = result.Startup; RefreshRows();
    }
    internal Task ChangeAsync(CalmRow row, bool undo = false) => OperateAsync(async () =>
    {
        await Task.Run(() =>
        {
            if (undo) changes.Undo(row.Reading.Setting.Id);
            else changes.Apply(row.Reading.Setting, !row.IsOn,
                row.IsSearch ? row.SelectedSearch : null);
        });
        RestartSuggested |= row.Reading.Setting.ExplorerRestart;
        await CalmWindows.BroadcastAsync([row.Reading.Setting.Broadcast]);
        await ReloadAfterChangeAsync();
    });
    internal Task StartupAsync(CalmStartupRow row, bool undo = false) => OperateAsync(async () =>
    {
        await Task.Run(() => { if (undo) changes.Undo(row.Entry.Id); else changes.SetStartup(row.Entry, row.IsOn); });
        await CalmWindows.BroadcastAsync(["Policy"]);
        await ReloadAfterChangeAsync();
    });
    [RelayCommand(AllowConcurrentExecutions = true)] private async Task CalmAllAsync() => await OperateAsync(async () =>
    {
        var findings = readings.Where(r => r.IsFinding).ToArray();
        if (findings.Length == 0) return;
        if (!await parent.Ui.ConfirmAsync("Calm these Windows settings?", string.Join("\n", findings.Select(r => "• " + r.Setting.Title)) + "\n\nStartup apps will not change. You can undo each change.", "Calm all")) return;
        // Re-read at the click boundary; do not overwrite a setting newly customized since the scan.
        var fresh = await Task.Run(() => CalmEvaluator.Scan(registry, CalmWindows.Build(registry)));
        var selected = fresh.Where(r => findings.Any(f => f.Setting.Id == r.Setting.Id)).ToArray();
        await Task.Run(() => changes.CalmAll(selected));
        RestartSuggested |= selected.Any(r => r.IsFinding && r.Setting.ExplorerRestart);
        await CalmWindows.BroadcastAsync(selected.Where(r => r.IsFinding).Select(r => r.Setting.Broadcast));
        await ReloadAfterChangeAsync();
    });
    [RelayCommand(AllowConcurrentExecutions = true)] private async Task UndoAllAsync() => await OperateAsync(async () =>
    {
        if (!await parent.Ui.ConfirmAsync("Undo all FlowDesk changes?", "Restore the exact values saved before FlowDesk changed Windows settings and startup apps. This may replace changes made since then in Windows Settings.", "Undo changes")) return;
        await Task.Run(changes.UndoAll);
        RestartSuggested = true;
        await CalmWindows.BroadcastAsync(["Policy", "TraySettings", "ImmersiveColorSet"]);
        await ReloadAfterChangeAsync();
    });
    [RelayCommand(AllowConcurrentExecutions = true)] private async Task RestartExplorerAsync() => await OperateAsync(async () =>
    {
        if (!await parent.Ui.ConfirmAsync("Restart File Explorer?", "The taskbar and desktop will briefly disappear. File Explorer windows may close. Finish any file operations first.", "Restart File Explorer")) return;
        await CalmWindows.RestartExplorerAsync(); RestartSuggested = false;
    });
    [RelayCommand] private async Task OpenStartupSettingsAsync()
    {
        if (!Busy) await Windows.System.Launcher.LaunchUriAsync(new("ms-settings:startupapps"));
    }
    [RelayCommand] private async Task OpenUpdateSettingsAsync()
    {
        if (!Busy) await Windows.System.Launcher.LaunchUriAsync(new("ms-settings:windowsupdate-options"));
    }
    private async Task OperateAsync(Func<Task> action)
    {
        if (Busy) return;
        // Do not disable the focused XAML subtree or command while awaiting I/O. The UI-thread
        // guard serializes writes; row setters reject input while retaining the current binding.
        Busy = true; ErrorOpen = false;
        try { await action(); }
        catch (Exception)
        {
            Error = "The check-up or change could not finish. Any changes already made remain in the undo journal. Run check-up again or try Undo.";
            ErrorOpen = true;
            // Even after a partial apply, expose Undo without requiring a successful rescan.
            try { HasUndo = changes.HasAnyUndo; } catch { }
            if (HasScan) { try { await ReloadAfterChangeAsync(); } catch { } }
        }
        finally { Busy = false; }
    }
}
