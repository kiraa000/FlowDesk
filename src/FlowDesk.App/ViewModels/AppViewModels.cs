using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlowDesk.App.Autosave;
using FlowDesk.App.Services;
using FlowDesk.App.Tour;
using FlowDesk.Core;
using FlowDesk.Core.Model;
using FlowDesk.Core.Restore;
using FlowDesk.Core.Hotkeys;

namespace FlowDesk.App.ViewModels;

public enum AppPage { Workspaces, Details, Capture, Settings, Layouts, Focus, Hotkeys, Calm, Advanced }
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
    public HotkeysViewModel Hotkeys { get; }
    public CalmViewModel Calm { get; }
    public AdvancedViewModel Advanced { get; }
    private IReadOnlyDictionary<AppPage, bool> OptionalPages => new Dictionary<AppPage, bool> { [AppPage.Hotkeys] = Services.Settings.WindowShortcutsEnabled, [AppPage.Advanced] = Services.Settings.AdvancedModeEnabled };
    public bool IsAdvancedVisible => OptionalNavigation.Visible(AppPage.Advanced, OptionalPages);
    internal bool FocusAdvancedSettingRequested { get; set; }
    internal void OpenAdvancedFromCalm()
    {
        FocusAdvancedSettingRequested = !IsAdvancedVisible;
        Navigate(IsAdvancedVisible ? AppPage.Advanced : AppPage.Settings);
    }
    internal void RefreshAdvancedPages()
    {
        if (ResolvePage(currentPage) != currentPage) Navigate(AppPage.Workspaces);
        OnPropertyChanged(nameof(IsAdvancedVisible));
        Calm.RefreshAdvancedLink();
    }
    public bool IsHotkeysVisible => OptionalNavigation.Visible(AppPage.Hotkeys, OptionalPages);
    internal bool IsHotkeysPageOpen => currentPage == AppPage.Hotkeys;
    internal AppPage ResolvePage(AppPage page) => OptionalNavigation.Resolve(page, AppPage.Workspaces, OptionalPages);
    internal void RefreshInputPages()
    {
        if (ResolvePage(currentPage) != currentPage) Navigate(AppPage.Workspaces);
        OnPropertyChanged(nameof(IsHotkeysVisible)); Hotkeys.RefreshStatus();
    }
    public AutoSaveViewModel AutoSave { get; }
    public BrowserNoticeViewModel BrowserNotice { get; }
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
    private WorkspaceViewModel? activeWorkspace;
    private string? lastAutoSaveError;
    private AppPage currentPage = AppPage.Workspaces;
    private readonly StatusLifetime statusLifetime = new();
    partial void OnStatusOpenChanged(bool value) { if (!value) statusLifetime.Invalidate(); }

    public AppViewModels(AppServices services, IUserInteraction ui)
    {
        Services = services; Ui = ui;
        Workspaces = new(this); Details = new(this); Capture = new(this); Settings = new(this);
        Layouts = new(this); Focus = new(this); BrowserNotice = new(this); AutoSave = new(this);
        Hotkeys = new(this); Calm = new(this); Advanced = new(this);
        Services.StateChanged += Workspaces.InvalidateDeleteUndo;
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
        RefreshInputPages();
        RefreshAdvancedPages();
        if (IsAdvancedVisible) await Advanced.RefreshAsync();
        if (Services.RecoveryMessage is { } recovery) ShowStatus(recovery, StatusKind.Warning);
    }

    /// <summary>
    /// Resumes autosave for the workspace stored by the last successful Open. Called after the autosave loop
    /// has started, so the activation generation stays current. No restore, launch or window movement happens
    /// here: windows stay where they are and are bound lazily by autosave.
    /// </summary>
    internal async Task ResumeActiveWorkspaceAsync()
    {
        var outcome = await StartupResume.TryResumeAsync(
            Services.Settings.ActiveWorkspaceId,
            FindWorkspace,
            () => Task.Run(() => (IReadOnlyList<long>)Services.Windows.GetAllAppWindows().Select(w => w.Handle).ToArray()),
            Services.Settings.IsAutosaveEnabled,
            Services.Settings.AlwaysAddsNewWindows,
            id => Services.Switcher.MarkActive(id),
            id => activeWorkspace = WorkspacesCollection.FirstOrDefault(vm => vm.Model.Id == id),
            ActivateAutoSave,
            ClearStoredActiveWorkspace,
            message => ShowStatus(message, StatusKind.Warning));

        foreach (var workspace in WorkspacesCollection) workspace.RefreshSummary();
        RefreshBrowserNotice();
    }

    /// <summary>One activation shape for a successful Open and a startup resume.</summary>
    private void ActivateAutoSave(string workspaceId, IReadOnlyList<long> baseline,
        bool enabled, bool always, DateTimeOffset? savedAt) =>
        AutoSave.Coordinator.Activate(workspaceId, baseline, enabled, always, savedAt);

    private void ClearStoredActiveWorkspace()
    {
        if (Services.Settings.ActiveWorkspaceId is null) return;
        Services.Settings.ActiveWorkspaceId = null;
        Services.Changed();
    }
    public void Navigate(AppPage page)
    {
        page = ResolvePage(page);
        Details.Workspace?.CommitName();
        SetCurrentPage(page);
        NavigationRequested?.Invoke(page);
    }
    internal void SetCurrentPage(AppPage page) { currentPage = page; RefreshBrowserNotice(); }

    internal string? ActiveWorkspaceId => activeWorkspace?.Model.Id;

    internal Workspace? FindWorkspace(string id) =>
        Services.Data.Workspaces.FirstOrDefault(w => w.Id == id);

    internal string WorkspaceName(string id) => FindWorkspace(id)?.Name ?? "this workspace";

    internal void ApplyAutoSaveStatus(AutoSaveStatus status)
    {
        // A queued status from an older epoch must not overwrite a switch or off/on. A current Off is allowed.
        if (!AutoSave.Coordinator.IsCurrentGeneration(status.WorkspaceId, status.Generation)) return;
        var workspace = WorkspacesCollection.FirstOrDefault(w => w.Model.Id == status.WorkspaceId);
        if (workspace is null) return;
        workspace.SetAutosave(status);

        if (status.State == AutoSaveState.Failed)
        {
            if (status.Message is { } message && message != lastAutoSaveError)
            {
                lastAutoSaveError = message;
                ShowStatus(message, StatusKind.Error);
            }
        }
        else lastAutoSaveError = null;
    }

    internal void ShowAutoSaveProblem(string message) => ShowStatus(message, StatusKind.Warning);
    internal void RefreshActivePositions() => activeWorkspace?.RefreshPositions();
    internal void RefreshActiveItems()
    {
        activeWorkspace?.ReloadItems();
        RefreshBrowserNotice();
    }
    internal void RefreshAutoSaveAge()
    {
        foreach (var workspace in WorkspacesCollection) workspace.RefreshAutosaveText();
    }

    internal void RefreshBrowserNotice(WorkspaceViewModel? workspace = null) =>
        BrowserNotice.ShowFor(currentPage switch
        {
            AppPage.Details => Details.Workspace?.Model,
            AppPage.Capture => new Workspace { Items = Capture.Candidates.Where(c => c.Selected && c.CanSelect).Select(c => c.Source.Item).ToList() },
            _ => null
        });

    internal void SetAutosaveEnabled(WorkspaceViewModel workspace, bool enabled)
    {
        Services.Settings.SetAutosaveEnabled(workspace.Model.Id, enabled);
        Services.Changed();
        if (activeWorkspace?.Model.Id != workspace.Model.Id) return;
        // During a quiesced operation the resume path applies the new setting; enabling here would race it.
        if (IsBusy) return;
        if (!enabled) { AutoSave.Coordinator.SetEnabled(false); return; }
        _ = RebaselineAutoSaveAsync(workspace.Model.Id, AutoSave.Coordinator.Generation);
    }

    internal void SetAlwaysAddNewWindows(WorkspaceViewModel workspace, bool always)
    {
        Services.Settings.SetAlwaysAddNewWindows(workspace.Model.Id, always);
        Services.Changed();
        if (activeWorkspace?.Model.Id == workspace.Model.Id) AutoSave.Coordinator.SetAlwaysAdd(always);
    }

    internal void PersistAlwaysAdd(string workspaceId, bool always)
    {
        Services.Settings.SetAlwaysAddNewWindows(workspaceId, always);
        Services.Changed();
        WorkspacesCollection.FirstOrDefault(w => w.Model.Id == workspaceId)?.SetAlwaysAddFromSettings(always);
        if (activeWorkspace?.Model.Id == workspaceId) AutoSave.Coordinator.SetAlwaysAdd(always);
    }

    private async Task RebaselineAutoSaveAsync(string workspaceId, long generation)
    {
        try
        {
            var baseline = await Task.Run(() => Services.Windows.GetAllAppWindows().Select(w => w.Handle).ToArray());
            if (activeWorkspace?.Model.Id != workspaceId) return;
            if (IsBusy) return; // resume applies it with preserved state; disabled watchers may be suspended
            if (AutoSave.Coordinator.Generation != generation) return; // a switch/reopen owns the epoch now
            if (!Services.Settings.IsAutosaveEnabled(workspaceId)) return;
            AutoSave.Coordinator.SetEnabled(true, baseline);
        }
        catch (Exception ex)
        {
            ShowStatus("FlowDesk could not start keeping this workspace up to date. " + ex.Message, StatusKind.Error);
        }
    }
    internal void ApplyTheme() => ThemeChanged?.Invoke();
    internal string NextWorkspaceName() => UxDecisions.NextWorkspaceName(WorkspacesCollection.Select(w => w.Name));
    internal void RefreshDuplicateNames()
    {
        foreach (var workspace in WorkspacesCollection) workspace.RefreshDuplicateName();
    }
    internal void RestoreDeletedWorkspace(Workspace model, int index)
    {
        if (FindWorkspace(model.Id) is not null) return;
        index = Math.Clamp(index, 0, Services.Data.Workspaces.Count);
        Services.Data.Workspaces.Insert(index, model);
        WorkspacesCollection.Insert(index, new(model, this));
        Workspaces.UpdateEmpty(); RefreshDuplicateNames(); Services.Changed();
    }
    internal WorkspaceViewModel Add(Workspace workspace)
    {
        Services.Data.Workspaces.Add(workspace);
        var vm = new WorkspaceViewModel(workspace, this);
        WorkspacesCollection.Add(vm); Workspaces.UpdateEmpty(); Services.Changed();
        RefreshDuplicateNames();
        return vm;
    }
    internal void Edit(WorkspaceViewModel workspace)
    {
        Details.Workspace = workspace; Navigate(AppPage.Details);
        RefreshBrowserNotice();
    }
    internal void ShowStatus(string message, StatusKind kind = StatusKind.Information)
    {
        statusLifetime.Invalidate();
        StatusMessage = message; StatusKind = kind; StatusOpen = true;
        if (kind is StatusKind.Success or StatusKind.Information)
            _ = statusLifetime.DismissAsync(statusLifetime.Generation, () => Task.Delay(TimeSpan.FromSeconds(6)), Ui.Enqueue, () => StatusOpen = false);
    }
    internal void ShowPaletteNotice(PaletteNotice notice)
    {
        ShowStatus(notice.Message, notice.IsCopied ? StatusKind.Information : StatusKind.Warning);
        if (notice.DismissAfter is { } duration)
            _ = statusLifetime.DismissAsync(statusLifetime.Generation, () => Task.Delay(duration), Ui.Enqueue, () => StatusOpen = false);
    }
    /// <summary>
    /// Runs a UI command with autosave quiesced: its in-flight poll/save is cancelled and awaited, and no queued
    /// autosave commit may apply while the operation touches windows or the tracker. OpenAsync handles its own
    /// suspension so it can activate the new workspace inside the operation.
    /// </summary>
    internal async Task ExecuteAsync(Func<Task> operation, bool manageAutoSave = true)
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            if (manageAutoSave) await AutoSave.SuspendAsync();
            await operation();
        }
        catch (Exception ex) { ShowStatus("The operation could not finish. " + ex.Message, StatusKind.Error); }
        finally
        {
            if (manageAutoSave)
            {
                if (activeWorkspace is { } workspace
                    && Services.Switcher.ActiveWorkspaceId == workspace.Model.Id
                    && AutoSave.Coordinator.ActiveWorkspaceId == workspace.Model.Id)
                    ResumeExistingAutoSave(workspace);
                else
                    await ResumeWatchingActualAsync(showWarning: false);
            }
            IsBusy = false;
        }
    }

    /// <summary>Resumes the same activation after a quiesced command, keeping baseline and Not-now state.</summary>
    private void ResumeExistingAutoSave(WorkspaceViewModel workspace)
    {
        bool enabled = Services.Settings.IsAutosaveEnabled(workspace.Model.Id);
        bool always = Services.Settings.AlwaysAddsNewWindows(workspace.Model.Id);
        workspace.SetAutosaveEnabledFromSettings(enabled);
        workspace.SetAlwaysAddFromSettings(always);
        AutoSave.Coordinator.Resume(enabled, always);
    }

    /// <summary>
    /// Watches whatever workspace the switcher actually considers active. Used only after a switch failure where
    /// the activation identity may have changed; on failure it stops and reports instead.
    /// </summary>
    private async Task ResumeWatchingActualAsync(bool showWarning)
    {
        string? actualId = Services.Switcher.ActiveWorkspaceId;
        var workspace = actualId is null ? null : WorkspacesCollection.FirstOrDefault(w => w.Model.Id == actualId);
        if (workspace is null)
        {
            activeWorkspace = null;
            AutoSave.Coordinator.Deactivate();
            if (showWarning && actualId is not null)
                ShowStatus("Keeping the workspace up to date stopped because the active workspace could not be found.", StatusKind.Warning);
            return;
        }

        try
        {
            var baseline = await Task.Run(() => Services.Windows.GetAllAppWindows().Select(w => w.Handle).ToArray());
            activeWorkspace = workspace;
            lastAutoSaveError = null;
            ActivateAutoSave(workspace.Model.Id, baseline,
                Services.Settings.IsAutosaveEnabled(workspace.Model.Id),
                Services.Settings.AlwaysAddsNewWindows(workspace.Model.Id),
                workspace.Model.LayoutSavedAt);
        }
        catch (Exception ex)
        {
            activeWorkspace = null;
            AutoSave.Coordinator.Deactivate();
            ShowStatus("Keeping the workspace up to date stopped because open windows could not be read. " + ex.Message,
                showWarning ? StatusKind.Error : StatusKind.Warning);
        }
    }
    internal async Task OpenAsync(WorkspaceViewModel workspace) => await ExecuteAsync(async () =>
    {
        // Invalidate the UI identity and quiesce autosave BEFORE the core switch starts: an in-flight poll/save
        // must not observe or place the outgoing workspace's windows while the switcher is mutating the tracker.
        activeWorkspace = null;
        try
        {
            await AutoSave.SuspendAsync();
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

            var leave = Services.Settings.MinimizePrevious ? LeaveAction.Minimize : LeaveAction.Leave;
            // Launch timeout comes from Settings; the switcher forwards it through RestoreOptions.
            var options = new RestoreOptions
            {
                LaunchTimeout = TimeSpan.FromSeconds(AppSettings.ClampLaunchTimeout(Services.Settings.LaunchTimeoutSeconds))
            };
            var contexts = new ContextApplier(Services.AudioOutput, Services.DoNotDisturb, Focus, Services.LogContextFailure);
            var beforeContext = await contexts.ApplyAsync(target.Context, beforeRestore: true);
            var restoreReport = await Task.Run(() => Services.Switcher.SwitchToAsync(target, previous, leave, progress, default, options));
            var afterContext = await contexts.ApplyAsync(target.Context, beforeRestore: false);
            restoreReport = restoreReport with { Context = [.. beforeContext, .. afterContext] };
            report = restoreReport;
            workspace.Model.LastUsed = target.LastUsed;
            // The core switch succeeded: keep startup identity consistent even if reading the baseline below fails.
            Services.Settings.ActiveWorkspaceId = workspace.Model.Id;
            foreach (var vm in WorkspacesCollection) vm.RefreshSummary();
            Services.Changed(); HasReport = true;

            // Baseline is taken after the restore finishes: restored windows (including browser extras) are
            // never offered as "new", while anything the user opens next is.
            var baseline = await Task.Run(() => Services.Windows.GetAllAppWindows().Select(w => w.Handle).ToArray());
            activeWorkspace = workspace;
            lastAutoSaveError = null;
            ActivateAutoSave(workspace.Model.Id, baseline,
                Services.Settings.IsAutosaveEnabled(workspace.Model.Id),
                Services.Settings.AlwaysAddsNewWindows(workspace.Model.Id),
                workspace.Model.LayoutSavedAt);
            RefreshBrowserNotice();

            var (message, hasFailures) = RestoreReportText.Summary(workspace.Name, restoreReport);
            ShowStatus(message, hasFailures ? StatusKind.Warning : StatusKind.Success);
        }
        catch
        {
            await ResumeWatchingActualAsync(showWarning: true);
            throw;
        }
        finally { workspace.IsOpening = false; }
    }, manageAutoSave: false);
    internal async Task SavePositionsAsync(WorkspaceViewModel workspace) => await ExecuteAsync(async () =>
    {
        var clone = workspace.Model.Clone();
        var ids = await Task.Run(() => Services.Capture.SaveLayout(clone));
        foreach (var item in workspace.Model.Items) item.Window = clone.Items.First(i => i.Id == item.Id).Window;
        workspace.Model.LayoutSavedAt = clone.LayoutSavedAt;
        workspace.ReloadItems(); workspace.RefreshAutosaveText(); Services.Changed();
        ShowStatus($"Saved positions for {ids.Count} of {CountText.Format(clone.Items.Count, "item")}. Closed windows keep their saved positions.");
    });
    internal async Task DeleteAsync(WorkspaceViewModel workspace) => await ExecuteAsync(async () =>
    {
        if (!await Ui.ConfirmAsync("Delete workspace?",
            $"Delete {workspace.Name}? Your apps and files will not be deleted.", "Delete workspace")) return;
        int deletedIndex = Services.Data.Workspaces.IndexOf(workspace.Model);
        var deleted = workspace.Model.Clone();
        Services.Data.Workspaces.Remove(workspace.Model); WorkspacesCollection.Remove(workspace);
        Services.Tracker.ForgetWorkspace(workspace.Model.Id);
        Services.Switcher.ResetSession(workspace.Model.Id);
        Services.Settings.ClearActiveWorkspaceIfMatches(workspace.Model.Id);
        if (Services.Switcher.ActiveWorkspaceId == workspace.Model.Id) Services.Switcher.ClearActive();
        if (activeWorkspace == workspace)
        {
            activeWorkspace = null;
            AutoSave.Coordinator.Deactivate();
            BrowserNotice.Hide();
        }
        Workspaces.UpdateEmpty(); Services.Changed(); Navigate(AppPage.Workspaces);
        RefreshDuplicateNames(); Workspaces.OfferDeleteUndo(deleted, deletedIndex);
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
