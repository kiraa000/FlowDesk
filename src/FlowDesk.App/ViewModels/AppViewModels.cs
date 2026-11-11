using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlowDesk.App.Services;
using FlowDesk.App.Tour;
using FlowDesk.Core.Model;
using FlowDesk.Core.Restore;

namespace FlowDesk.App.ViewModels;

public enum AppPage { Workspaces, Details, Capture, Settings, Layouts, Focus }
public enum StatusKind { Information, Success, Warning, Error }

public partial class AppViewModels : ObservableObject
{
    internal AppServices Services { get; }
    internal IUserInteraction Ui { get; }
    public ObservableCollection<WorkspaceViewModel> WorkspacesCollection { get; } = [];
    public WorkspacesViewModel Workspaces { get; }
    public WorkspaceDetailsViewModel Details { get; }
    public SaveOpenWindowsViewModel Capture { get; }
    public SettingsViewModel Settings { get; }
    public LayoutsViewModel Layouts { get; }
    public FocusViewModel Focus { get; }
    public TourStateMachine Tour { get; } = new();
    public event Action<AppPage>? NavigationRequested;
    public event Action? ThemeChanged;
    /// <summary>Raised when the tour should (re)start: first run, or "Show the tour again".</summary>
    public event Action? TourRequested;
    [ObservableProperty] public partial bool IsBusy { get; set; }
    [ObservableProperty] public partial bool StatusOpen { get; set; }
    [ObservableProperty] public partial string StatusMessage { get; set; } = "";
    [ObservableProperty] public partial StatusKind StatusKind { get; set; }
    [ObservableProperty] public partial bool HasReport { get; set; }
    private RestoreReport? report;

    public AppViewModels(AppServices services, IUserInteraction ui)
    {
        Services = services; Ui = ui;
        Workspaces = new(this); Details = new(this); Capture = new(this); Settings = new(this);
        Layouts = new(this); Focus = new(this);
        Services.SaveFailed += message => Ui.Enqueue(() => ShowStatus(message, StatusKind.Error));
        Tour.Finished += () =>
        {
            Services.Data.TourCompleted = true;
            Services.Changed();
        };
    }
    /// <summary>Starts the tour at step 1 and lets the shell show the first tip.</summary>
    public void RequestTour()
    {
        Tour.Start();
        TourRequested?.Invoke();
    }
    public async Task LoadAsync()
    {
        await Services.LoadAsync();
        foreach (var workspace in Services.Data.Workspaces) WorkspacesCollection.Add(new(workspace, this));
        Workspaces.UpdateEmpty(); Settings.Load(); Focus.Load();
        if (Services.RecoveryMessage is { } recovery) ShowStatus(recovery, StatusKind.Warning);
    }
    public void Navigate(AppPage page)
    {
        Details.Workspace?.CommitName();
        NavigationRequested?.Invoke(page);
    }
    internal void ApplyTheme() => ThemeChanged?.Invoke();
    internal WorkspaceViewModel Add(Workspace workspace)
    {
        Services.Data.Workspaces.Add(workspace);
        var vm = new WorkspaceViewModel(workspace, this);
        WorkspacesCollection.Add(vm); Workspaces.UpdateEmpty(); Services.Changed();
        return vm;
    }
    internal void Edit(WorkspaceViewModel workspace)
    {
        Details.Workspace = workspace; Navigate(AppPage.Details);
    }
    internal void ShowStatus(string message, StatusKind kind = StatusKind.Information)
    {
        StatusMessage = message; StatusKind = kind; StatusOpen = true;
    }
    internal async Task ExecuteAsync(Func<Task> operation)
    {
        if (IsBusy) return;
        IsBusy = true;
        try { await operation(); }
        catch (Exception ex) { ShowStatus("The operation could not finish. " + ex.Message, StatusKind.Error); }
        finally { IsBusy = false; }
    }
    internal async Task OpenAsync(WorkspaceViewModel workspace) => await ExecuteAsync(async () =>
    {
        workspace.CommitName();
        var target = workspace.Model.Clone();
        var previous = Services.Data.Workspaces
            .FirstOrDefault(w => w.Id == Services.Switcher.ActiveWorkspaceId)?.Clone();
        workspace.IsOpening = true;
        workspace.OpeningText = $"Opening 0 of {target.Items.Count}";
        Navigate(AppPage.Workspaces);
        int count = 0;
        var progress = new UiProgress(Ui, item =>
        {
            if (item.Outcome == ItemOutcome.Pending) return; // each item reports Pending first, then its result
            workspace.OpeningText = $"Opening {++count} of {target.Items.Count} · {item.Name}: {item.Outcome}";
        });
        try
        {
            var leave = Services.Settings.MinimizePrevious ? LeaveAction.Minimize : LeaveAction.Leave;
            // Launch timeout comes from Settings; the switcher forwards it through RestoreOptions.
            var options = new RestoreOptions
            {
                LaunchTimeout = TimeSpan.FromSeconds(AppSettings.ClampLaunchTimeout(Services.Settings.LaunchTimeoutSeconds))
            };
            report = await Task.Run(() => Services.Switcher.SwitchToAsync(target, previous, leave, progress, default, options));
            workspace.Model.LastUsed = target.LastUsed;
            foreach (var vm in WorkspacesCollection) vm.RefreshSummary();
            Services.Changed(); HasReport = true;
            var missing = report.Items.Where(i => i.Outcome is not ItemOutcome.Launched and not ItemOutcome.Reused);
            var detail = string.Join(" ", missing.Select(i => $"{i.Name}: {i.Detail ?? i.Outcome.ToString()}"));
            ShowStatus($"{workspace.Name}: {report.Placed} of {report.Items.Count} windows in place. {detail}",
                report.AllPlaced ? StatusKind.Success : StatusKind.Warning);
        }
        finally { workspace.IsOpening = false; }
    });
    internal async Task SavePositionsAsync(WorkspaceViewModel workspace) => await ExecuteAsync(async () =>
    {
        var clone = workspace.Model.Clone();
        var ids = await Task.Run(() => Services.Capture.SaveLayout(clone));
        foreach (var item in workspace.Model.Items) item.Window = clone.Items.First(i => i.Id == item.Id).Window;
        workspace.Model.LayoutSavedAt = clone.LayoutSavedAt;
        workspace.ReloadItems(); Services.Changed();
        ShowStatus($"Saved positions for {ids.Count} of {clone.Items.Count} items. Closed windows keep their saved positions.");
    });
    internal async Task DeleteAsync(WorkspaceViewModel workspace) => await ExecuteAsync(async () =>
    {
        if (!await Ui.ConfirmAsync("Delete workspace?",
            $"Delete {workspace.Name}? Your apps and files will not be deleted.", "Delete workspace")) return;
        Services.Data.Workspaces.Remove(workspace.Model); WorkspacesCollection.Remove(workspace);
        Services.Switcher.ResetSession(workspace.Model.Id);
        Workspaces.UpdateEmpty(); Services.Changed(); Navigate(AppPage.Workspaces);
    });
    [RelayCommand]
    private async Task ShowReportAsync()
    {
        if (report is not null) await Ui.ShowReportAsync(report);
    }
    private sealed class UiProgress(IUserInteraction ui, Action<ItemResult> receive) : IProgress<ItemResult>
    {
        public void Report(ItemResult value) => ui.Enqueue(() => receive(value));
    }
}
