using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlowDesk.App.Services;
using FlowDesk.Core.Capture;
using FlowDesk.Core.Model;
using FlowDesk.Core.Windows;

namespace FlowDesk.App.ViewModels;

public partial class WorkspacesViewModel(AppViewModels owner) : ObservableObject
{
    public ObservableCollection<WorkspaceViewModel> Workspaces => owner.WorkspacesCollection;
    public bool IsEmpty => Workspaces.Count == 0;
    public bool HasWorkspaces => !IsEmpty;
    internal void UpdateEmpty() { OnPropertyChanged(nameof(IsEmpty)); OnPropertyChanged(nameof(HasWorkspaces)); }
    [RelayCommand]
    private async Task NewFromWindowsAsync()
    {
        owner.Capture.Name = $"Workspace {Workspaces.Count + 1}";
        owner.Navigate(AppPage.Capture); await owner.Capture.RefreshCommand.ExecuteAsync(null);
    }
    [RelayCommand]
    private void NewEmpty() => owner.Edit(owner.Add(new Workspace { Name = $"Workspace {Workspaces.Count + 1}" }));
}

public partial class WorkspaceDetailsViewModel(AppViewModels owner) : ObservableObject
{
    [ObservableProperty] public partial WorkspaceViewModel? Workspace { get; set; }
    public string[] Breadcrumbs => ["Workspaces", Workspace?.Name ?? "Workspace"];
    public ColorOption[] ColorOptions { get; } =
    [
        new("Blue", "blue"), new("Green", "green"), new("Orange", "orange"), new("Red", "red")
    ];
    public GlyphOption[] GlyphOptions { get; } =
    [
        new("Workspace", "\uE8A1"), new("Study", "\uE82D"), new("Work", "\uE821"), new("Code", "\uE943")
    ];
    partial void OnWorkspaceChanged(WorkspaceViewModel? oldValue, WorkspaceViewModel? newValue)
    {
        if (oldValue is not null) oldValue.PropertyChanged -= WorkspaceChanged;
        if (newValue is not null) newValue.PropertyChanged += WorkspaceChanged;
        OnPropertyChanged(nameof(Breadcrumbs));
        owner.RefreshBrowserNotice(newValue);
    }
    private void WorkspaceChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(WorkspaceViewModel.Name)) OnPropertyChanged(nameof(Breadcrumbs));
    }
    [RelayCommand] private void Back() { Workspace?.CommitName(); owner.Navigate(AppPage.Workspaces); }
    [RelayCommand] private Task AddAppAsync() => AddPathAsync(ItemKind.App);
    [RelayCommand] private Task AddFileAsync() => AddPathAsync(ItemKind.File);
    [RelayCommand] private Task AddFolderAsync() => AddPathAsync(ItemKind.Folder);
    private async Task AddPathAsync(ItemKind kind) => await owner.ExecuteAsync(async () =>
    {
        if (Workspace is not { } workspace) return;
        var path = await owner.Ui.PickPathAsync(kind);
        if (path is null) return;
        var item = new WorkspaceItem { Kind = kind, Name = Path.GetFileName(path.TrimEnd('\\')) };
        if (kind == ItemKind.App) item.App = new AppRef { ExePath = path }; else item.Path = path;
        workspace.Model.Items.Add(item); workspace.ReloadItems(); owner.Services.Changed();
        owner.RefreshBrowserNotice(workspace);
    });
    [RelayCommand]
    private async Task AddWebsiteAsync() => await owner.ExecuteAsync(async () =>
    {
        if (Workspace is not { } workspace) return;
        var item = await owner.Ui.NewWebsiteAsync();
        if (item is null) return;
        workspace.Model.Items.Add(item); workspace.ReloadItems(); owner.Services.Changed();
        owner.RefreshBrowserNotice(workspace);
    });
    [RelayCommand]
    private async Task AddRunningAsync() => await owner.ExecuteAsync(async () =>
    {
        if (Workspace is not { } workspace) return;
        var candidates = await Task.Run(() => owner.Services.Capture.CaptureOpenWindows());
        var available = candidates.Where(c => c.Problem is null && c.Item.App is not null).ToArray();
        if (available.Length == 0)
        {
            owner.ShowStatus("No running apps can be added. Browse for an executable instead."); return;
        }
        var selected = await owner.Ui.PickRunningAsync(available);
        if (selected is null) return;
        workspace.Model.Items.Add(selected.Item.Clone()); workspace.ReloadItems(); owner.Services.Changed();
        owner.RefreshBrowserNotice(workspace);
    });
}

public sealed record GlyphOption(string Label, string Glyph);

public sealed record ColorOption(string Label, string Value);

public partial class SaveOpenWindowsViewModel(AppViewModels owner) : ObservableObject
{
    [ObservableProperty] public partial string Name { get; set; } = "Workspace 1";
    [ObservableProperty] public partial bool IsLoading { get; set; }
    [ObservableProperty] public partial string Error { get; set; } = "";
    public bool HasError => !string.IsNullOrEmpty(Error);
    public bool IsEmpty => !IsLoading && Candidates.Count == 0;
    public ObservableCollection<CandidateViewModel> Candidates { get; } = [];
    partial void OnErrorChanged(string value) => OnPropertyChanged(nameof(HasError));
    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(IsEmpty));
    [RelayCommand]
    private async Task RefreshAsync() => await owner.ExecuteAsync(async () =>
    {
        IsLoading = true; Error = "";
        try
        {
            var captured = await Task.Run(() => owner.Services.Capture.CaptureOpenWindows());
            Candidates.Clear(); foreach (var candidate in captured) Candidates.Add(new(candidate));
        }
        finally { IsLoading = false; }
    });
    [RelayCommand]
    private void Save()
    {
        Error = "";
        if (string.IsNullOrWhiteSpace(Name)) { Error = "Enter a workspace name."; return; }
        var selected = Candidates.Where(c => c.Selected && c.CanSelect).ToArray();
        if (selected.Length == 0) { Error = "Select at least one window, or create an empty workspace from Workspaces."; return; }
        List<WorkspaceItem> items = [];
        List<(WorkspaceItem Item, WindowInfo Window)> saved = [];
        foreach (var candidate in selected)
        {
            var item = candidate.Source.Item.Clone();
            if (candidate.IsBrowser && !string.IsNullOrWhiteSpace(candidate.Links))
            {
                if (!LinkValidation.TryParse(candidate.Links, out var urls))
                { Error = "Use a complete http or https link on each line."; return; }
                item.Kind = ItemKind.Website; item.Urls = urls;
            }
            items.Add(item); saved.Add((item, candidate.Source.Window));
        }
        var workspace = owner.Add(new Workspace { Name = Name.Trim(), Items = items });
        // The workspace now owns these exact windows: the first Open reuses them instead of matching
        // identical titles against windows that belong to another workspace.
        SaveOpenWindowsBinding.Bind(owner.Services.Tracker, workspace.Model, saved);
        owner.Edit(workspace);
    }
    [RelayCommand] private void Cancel() => owner.Navigate(AppPage.Workspaces);
}

public partial class CandidateViewModel : ObservableObject
{
    internal CaptureCandidate Source { get; }
    [ObservableProperty] public partial bool Selected { get; set; }
    [ObservableProperty] public partial string Links { get; set; } = "";
    public bool CanSelect => Source.Problem is null;
    public bool HasProblem => Source.Problem is not null;
    public bool HasNoProblem => Source.Problem is null;
    public string Name => Source.Item.Name;
    public string Title => Source.Window.Title;
    public string Position => ItemDisplay.Position(Source.Item);
    public string Problem => Source.Problem ?? "";
    /// <summary>
    /// The single secondary line of a compact row: why it cannot be picked, else the window title,
    /// else its position. A title that merely repeats the name is not shown (it would be noise).
    /// </summary>
    public string Secondary => HasProblem ? Problem
        : !string.IsNullOrWhiteSpace(Title) && !string.Equals(Title.Trim(), Name.Trim(), StringComparison.OrdinalIgnoreCase)
            ? Title : Position;
    public bool IsBrowser => Source.IsBrowser;
    public string ExePath => Source.Window.ExePath;
    /// <summary>Stable id for the trusted real-app script: selects this exact window, not a same-name twin.</summary>
    public string AutomationId => "window-" + Source.Window.Handle.ToString(System.Globalization.CultureInfo.InvariantCulture);
    public string Glyph => ItemDisplay.Glyph(Source.Item.Kind);
    public CandidateViewModel(CaptureCandidate candidate) { Source = candidate; Selected = CanSelect; }
}

public partial class SettingsViewModel(AppViewModels owner) : ObservableObject
{
    public string[] Themes { get; } = ["System", "Light", "Dark"];
    [ObservableProperty] public partial string Theme { get; set; } = "System";
    [ObservableProperty] public partial bool MinimizePrevious { get; set; } = true;
    [ObservableProperty] public partial double LaunchTimeout { get; set; } = AppSettings.DefaultLaunchTimeoutSeconds;
    [ObservableProperty] public partial bool StartWithWindows { get; set; }
    [ObservableProperty] public partial bool WindowShortcuts { get; set; }
    [ObservableProperty] public partial string WindowShortcutsStatus { get; set; } = "";
    [ObservableProperty] public partial bool WindowShortcutsDesktopUnavailable { get; set; }
    [ObservableProperty] public partial bool WindowShortcutsBusy { get; set; }
    [ObservableProperty] public partial bool AutoHotkeyRunning { get; set; }
    [ObservableProperty] public partial string ShortcutConflicts { get; set; } = "";
    public bool HasShortcutConflicts => ShortcutConflicts.Length > 0;
    /// <summary>False while a start/stop is pending, so the toggle cannot race its own transition.</summary>
    public bool WindowShortcutsCanToggle => !WindowShortcutsBusy;
    /// <summary>Plain-word list of every binding, shown in the collapsed Shortcut list expander.</summary>
    public IReadOnlyList<string> ShortcutLines { get; } =
        FlowDesk.Core.Hotkeys.HotkeyCatalog.All
            .Select(binding => $"{binding.Shortcut}  ·  {binding.Label}")
            .ToArray();
    /// <summary>One line for the About card, read from the app assembly.</summary>
    public string VersionText { get; } = $"FlowDesk {AppVersion.Display}";
    private bool loading;
    internal void Load()
    {
        loading = true;
        Theme = owner.Services.Settings.Theme;
        MinimizePrevious = owner.Services.Settings.MinimizePrevious;
        LaunchTimeout = AppSettings.ClampLaunchTimeout(owner.Services.Settings.LaunchTimeoutSeconds);
        StartWithWindows = StartupRegistration.IsEnabled();
        WindowShortcuts = owner.Services.Settings.WindowShortcutsEnabled;
        RefreshWindowShortcutsState();
        loading = false; owner.ApplyTheme();
        _ = RefreshAutoHotkeyAsync();
    }

    partial void OnShortcutConflictsChanged(string value) => OnPropertyChanged(nameof(HasShortcutConflicts));

    partial void OnWindowShortcutsBusyChanged(bool value) => OnPropertyChanged(nameof(WindowShortcutsCanToggle));

    partial void OnWindowShortcutsChanged(bool value)
    {
        if (loading) return;
        if (WindowShortcutsBusy)
        {
            loading = true;
            WindowShortcuts = !value;
            loading = false;
            return;
        }
        owner.Services.Settings.WindowShortcutsEnabled = value;
        owner.Services.Changed();
        _ = ApplyWindowShortcutsAsync(value);
    }

    /// <summary>Refreshes the lines under the toggle from the live host and backend.</summary>
    internal void RefreshWindowShortcutsState()
    {
        var hotkeys = owner.Services.Hotkeys;
        string status = hotkeys.DesktopStatus;
        if (hotkeys.IsRunning && !hotkeys.WheelAvailable)
            status += " Alt+Shift+Wheel resize is unavailable.";
        WindowShortcutsStatus = status;
        WindowShortcutsDesktopUnavailable = !hotkeys.DesktopsAvailable;
        var conflicts = hotkeys.Conflicts;
        ShortcutConflicts = conflicts.Count == 0 ? "" : "In use by another app: " + string.Join(", ", conflicts);
    }

    private async Task ApplyWindowShortcutsAsync(bool enabled)
    {
        if (WindowShortcutsBusy) return;
        WindowShortcutsBusy = true;
        try
        {
            if (enabled) await owner.Services.Hotkeys.StartAsync();
            else await owner.Services.Hotkeys.StopAsync();
        }
        catch (Exception ex)
        {
            // Keep the toggle and the saved setting in step with what actually runs.
            loading = true;
            WindowShortcuts = false;
            loading = false;
            owner.Services.Settings.WindowShortcutsEnabled = false;
            owner.Services.Changed();
            owner.ShowStatus("Window shortcuts could not start. " + ex.Message, StatusKind.Warning);
        }
        finally
        {
            WindowShortcutsBusy = false;
            RefreshWindowShortcutsState();
            await RefreshAutoHotkeyAsync();
        }
    }

    [RelayCommand]
    private async Task ChooseShortcutsDllAsync()
    {
        if (WindowShortcutsBusy) return;
        var source = await owner.Ui.PickFileAsync(".dll");
        if (string.IsNullOrEmpty(source)) return;

        string nativeFolder = Path.Combine(owner.Services.Store.Folder, "native");
        string target = Path.Combine(nativeFolder, FlowDesk.Core.Desktops.VirtualDesktopAccessor.DllName);
        if (File.Exists(target) && !await owner.Ui.ConfirmAsync(
                "Replace VirtualDesktopAccessor.dll?",
                "A desktop shortcut file already exists in the FlowDesk data folder. Replace it with the one you chose?",
                "Replace"))
        {
            return;
        }

        WindowShortcutsBusy = true;
        try
        {
            await Task.Run(() =>
            {
                Directory.CreateDirectory(nativeFolder);
                string temporary = target + ".tmp";
                try
                {
                    File.Copy(source, temporary, overwrite: true);
                    File.Move(temporary, target, overwrite: true);
                }
                finally
                {
                    if (File.Exists(temporary)) File.Delete(temporary);
                }
            });
            await owner.Services.Hotkeys.ReloadBackendAsync();
            RefreshWindowShortcutsState();
            if (!owner.Services.Hotkeys.DesktopsAvailable)
                owner.ShowStatus(owner.Services.Hotkeys.DesktopStatus, StatusKind.Warning);
        }
        catch (Exception ex)
        {
            owner.ShowStatus("The desktop shortcut file could not be installed. " + ex.Message, StatusKind.Error);
        }
        finally
        {
            WindowShortcutsBusy = false;
        }
    }

    /// <summary>AutoHotkey wins over RegisterHotKey, so it is worth telling the user when it runs.</summary>
    private async Task RefreshAutoHotkeyAsync()
    {
        bool running = false;
        try
        {
            running = await Task.Run(() =>
            {
                bool found = false;
                foreach (var process in Process.GetProcesses())
                {
                    try
                    {
                        if (process.ProcessName.StartsWith("AutoHotkey", StringComparison.OrdinalIgnoreCase)) found = true;
                    }
                    catch (Exception) { /* the process may exit while its name is read */ }
                    finally { process.Dispose(); } // dispose every wrapper, including after a match
                }
                return found;
            });
        }
        catch (Exception) { /* process enumeration is best effort */ }
        AutoHotkeyRunning = running;
    }
    partial void OnThemeChanged(string value)
    {
        owner.Services.Settings.Theme = value; owner.ApplyTheme();
        if (!loading) owner.Services.Changed();
    }
    partial void OnMinimizePreviousChanged(bool value)
    {
        owner.Services.Settings.MinimizePrevious = value;
        if (!loading) owner.Services.Changed();
    }
    partial void OnLaunchTimeoutChanged(double value)
    {
        // NumberBox can hand over NaN (cleared box), infinity, out-of-range or fractional values.
        // Fall back to the last valid setting, then snap the box to the supported range.
        int seconds = LaunchTimeoutValue.Normalize(value, owner.Services.Settings.LaunchTimeoutSeconds);
        bool wasLoading = loading;
        if (!double.IsFinite(value) || Math.Abs(value - seconds) > 0.001)
        {
            loading = true; LaunchTimeout = seconds; loading = wasLoading;
        }
        owner.Services.Settings.LaunchTimeoutSeconds = seconds;
        if (!wasLoading) owner.Services.Changed();
    }
    partial void OnStartWithWindowsChanged(bool value)
    {
        if (loading) return;
        if (StartupRegistration.SetEnabled(value)) return;
        loading = true; StartWithWindows = !value; loading = false;
        owner.ShowStatus("FlowDesk could not change the Windows startup setting. Check access to your user registry.",
            StatusKind.Error);
    }
    [RelayCommand] private void ReplayTour() => owner.RequestTour();
    [RelayCommand]
    private void OpenDataFolder()
    {
        try
        {
            Directory.CreateDirectory(owner.Services.Store.Folder);
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{owner.Services.Store.Folder}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            owner.ShowStatus("The data folder could not be opened. " + ex.Message, StatusKind.Error);
        }
    }
    [RelayCommand]
    private void OpenLogFolder()
    {
        try
        {
            Directory.CreateDirectory(CrashLog.DefaultDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{CrashLog.DefaultDirectory}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            owner.ShowStatus("The log folder could not be opened. " + ex.Message, StatusKind.Error);
        }
    }
}

public static class LinkValidation
{
    public static bool TryParse(string input, out List<string> urls)
    {
        urls = input.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct().ToList();
        return urls.Count > 0 && urls.All(s => Uri.TryCreate(s, UriKind.Absolute, out var uri)
            && (uri.Scheme == "https" || uri.Scheme == "http") && !string.IsNullOrWhiteSpace(uri.Host));
    }
}
