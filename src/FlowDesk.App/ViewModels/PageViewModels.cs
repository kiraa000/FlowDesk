using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlowDesk.App.Services;
using FlowDesk.Core.Capture;
using FlowDesk.Core.Model;

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
    });
    [RelayCommand]
    private async Task AddWebsiteAsync() => await owner.ExecuteAsync(async () =>
    {
        if (Workspace is not { } workspace) return;
        var item = await owner.Ui.NewWebsiteAsync();
        if (item is null) return;
        workspace.Model.Items.Add(item); workspace.ReloadItems(); owner.Services.Changed();
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
        foreach (var candidate in selected)
        {
            var item = candidate.Source.Item.Clone();
            if (candidate.IsBrowser && !string.IsNullOrWhiteSpace(candidate.Links))
            {
                if (!LinkValidation.TryParse(candidate.Links, out var urls))
                { Error = "Use a complete http or https link on each line."; return; }
                item.Kind = ItemKind.Website; item.Urls = urls;
            }
            items.Add(item);
        }
        owner.Edit(owner.Add(new Workspace { Name = Name.Trim(), Items = items }));
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
    /// <summary>One line for the About card, read from the app assembly.</summary>
    public string VersionText { get; } =
        $"FlowDesk {Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "1.0.0"}";
    private bool loading;
    internal void Load()
    {
        loading = true;
        Theme = owner.Services.Settings.Theme;
        MinimizePrevious = owner.Services.Settings.MinimizePrevious;
        LaunchTimeout = AppSettings.ClampLaunchTimeout(owner.Services.Settings.LaunchTimeoutSeconds);
        StartWithWindows = StartupRegistration.IsEnabled();
        loading = false; owner.ApplyTheme();
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
