using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlowDesk.Core.Capture;
using FlowDesk.Core.Layout;
using FlowDesk.Core.Windows;
using FlowDesk.App.Services;

namespace FlowDesk.App.ViewModels;

public sealed record LayoutChoice(string Name, LayoutPreset Preset)
{
    public override string ToString() => Name;
}
public partial class MonitorChoice(string name, string device, bool isPrimary) : ObservableObject
{
    public string Name => name;
    public string Device => device;
    public bool IsPrimary => isPrimary;
    [ObservableProperty] public partial bool IsSelected { get; set; }
    public override string ToString() => Name;
}
public partial class LayoutWindow(string name, WindowInfo window) : ObservableObject
{
    public string Name => name;
    public WindowInfo Window => window;
    [ObservableProperty] public partial bool IsSelected { get; set; } = true;
    public string Detail => Window.Title;
    public string ExePath => Window.ExePath;
    public override string ToString() => Name;
}

public partial class LayoutsViewModel(AppViewModels owner) : ObservableObject
{
    public LayoutChoice[] Presets { get; } = [new("Two columns", LayoutPreset.TwoColumns),
        new("Primary + sidebar", LayoutPreset.PrimarySidebar), new("Grid", LayoutPreset.Grid), new("Cascade", LayoutPreset.Cascade)];
    [ObservableProperty] public partial MonitorChoice[] Monitors { get; set; } = [];
    public ObservableCollection<LayoutWindow> Windows { get; } = [];
    [ObservableProperty] public partial LayoutChoice? SelectedPreset { get; set; }
    [ObservableProperty] public partial bool HasWindows { get; set; }
    [ObservableProperty] public partial bool CanSave { get; set; }
    public bool IsEmpty => !HasWindows;
    public bool MultipleMonitors => Monitors.Length > 1;
    public bool HasActiveWorkspace => workspace is not null && workspace.Model.Id == owner.Services.Switcher.ActiveWorkspaceId;
    public bool CanApply => Windows.Any(w => w.IsSelected);

    /// <summary>"Display 2 · 2560 × 1440 · Main" instead of the raw device name \\.\DISPLAY2.</summary>
    public static string MonitorLabel(string device, bool isPrimary, int width, int height)
    {
        string name = device;
        int index = device.LastIndexOf("DISPLAY", StringComparison.OrdinalIgnoreCase);
        if (index >= 0 && int.TryParse(device[(index + "DISPLAY".Length)..], out int number))
            name = $"Display {number}";
        string label = $"{name} · {width} × {height}";
        return isPrimary ? label + " · Main" : label;
    }

    private WorkspaceViewModel? workspace;
    private IReadOnlyList<WindowInfo> liveWindows = [];
    private readonly Dictionary<long, bool> selections = [];
    private void UpdateWindows()
    {
        foreach (var row in Windows) { selections[row.Window.Handle] = row.IsSelected; row.PropertyChanged -= SelectionChanged; }
        Windows.Clear();
        var monitor = Monitors.FirstOrDefault(m => m.IsSelected)?.Device;
        foreach (var window in liveWindows.Where(w => LayoutSelection.Includes(w, monitor, owner.Services.Windows.OwnProcessId)))
        {
            var row = new LayoutWindow(string.IsNullOrEmpty(window.ExeName) ? window.Title : window.ExeName, window)
                { IsSelected = selections.GetValueOrDefault(window.Handle, true) };
            row.PropertyChanged += SelectionChanged;
            Windows.Add(row);
        }
        HasWindows = Windows.Count > 0; CanSave = false;
        OnPropertyChanged(nameof(CanApply)); OnPropertyChanged(nameof(HasActiveWorkspace));
    }
    private void SelectionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(LayoutWindow.IsSelected)) return;
        OnPropertyChanged(nameof(CanApply)); CanSave = false;
    }
    private void MonitorChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MonitorChoice.IsSelected) || sender is not MonitorChoice { IsSelected: true } selected) return;
        foreach (var monitor in Monitors.Where(m => m != selected)) monitor.IsSelected = false;
        UpdateWindows();
    }
    partial void OnHasWindowsChanged(bool value) => OnPropertyChanged(nameof(IsEmpty));
    [RelayCommand] private void OpenWorkspace() => owner.Navigate(AppPage.Workspaces);
    [RelayCommand] private async Task RefreshAsync() => await owner.ExecuteAsync(async () =>
    {
        workspace = owner.WorkspacesCollection.FirstOrDefault(w => w.Model.Id == owner.Services.Switcher.ActiveWorkspaceId);
        var result = await Task.Run(() => (monitors: owner.Services.Windows.GetMonitors(),
            live: owner.Services.Windows.GetAppWindows()));
        var device = Monitors.FirstOrDefault(m => m.IsSelected)?.Device;
        var monitors = result.monitors.Select(m =>
            new MonitorChoice(MonitorLabel(m.Device, m.IsPrimary, m.Bounds.Width, m.Bounds.Height), m.Device, m.IsPrimary)).ToArray();
        if (monitors.Length > 0) (monitors.FirstOrDefault(m => m.Device == device) ?? monitors[0]).IsSelected = true;
        foreach (var old in Monitors) old.PropertyChanged -= MonitorChanged;
        Monitors = monitors; liveWindows = result.live;
        foreach (var m in Monitors) m.PropertyChanged += MonitorChanged;
        UpdateWindows();
        SelectedPreset ??= Presets[0];
        OnPropertyChanged(nameof(MultipleMonitors));
    });
    [RelayCommand] private async Task ApplyAsync() => await owner.ExecuteAsync(async () =>
    {
        if (!CanApply || SelectedPreset is null) return;
        var ordered = Windows.Where(w => w.IsSelected).Select(w => w.Window).ToArray();
        var preset = SelectedPreset.Preset; var monitor = Monitors.FirstOrDefault(m => m.IsSelected)?.Device;
        var results = await Task.Run(() => owner.Services.Layouts.Apply(preset, ordered, monitor));
        CanSave = HasActiveWorkspace && results.Any(r => r.Outcome == PlaceOutcome.Ok);
        int placed = results.Count(r => r.Outcome == PlaceOutcome.Ok);
        owner.ShowStatus($"Placed {placed} of {results.Count} windows. Windows running as administrator may need elevated access.",
            placed == results.Count ? StatusKind.Success : StatusKind.Warning);
    });
    [RelayCommand] private async Task SaveAsync()
    {
        if (HasActiveWorkspace && workspace is not null && CanSave) await owner.SavePositionsAsync(workspace);
    }
}
