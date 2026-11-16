using CommunityToolkit.Mvvm.ComponentModel;
using FlowDesk.Core.HotCorners;

namespace FlowDesk.App.ViewModels;

public partial class SettingsViewModel
{
    [ObservableProperty] public partial bool HotCorners { get; set; }
    [ObservableProperty] public partial bool HotCornersBusy { get; set; }
    [ObservableProperty] public partial int HotCornerTopLeftIndex { get; set; } = 1;
    [ObservableProperty] public partial int HotCornerTopRightIndex { get; set; }
    [ObservableProperty] public partial int HotCornerBottomLeftIndex { get; set; }
    [ObservableProperty] public partial int HotCornerBottomRightIndex { get; set; } = 2;
    public bool HotCornersCanToggle => !HotCornersBusy;
    public IReadOnlyList<HotCornerActionOption> HotCornerActions => HotCornerConfiguration.Actions;
    private bool hotCornersLoading;
    private static int Index(HotCornerAction action) => Math.Max(0, HotCornerConfiguration.Actions.ToList().FindIndex(x => x.Action == action));
    private static HotCornerAction ActionAt(int index) => index >= 0 && index < HotCornerConfiguration.Actions.Count
        ? HotCornerConfiguration.Actions[index].Action : HotCornerAction.None;
    private HotCornerConfiguration CornerConfiguration => new(HotCorners, ActionAt(HotCornerTopLeftIndex), ActionAt(HotCornerTopRightIndex),
        ActionAt(HotCornerBottomLeftIndex), ActionAt(HotCornerBottomRightIndex));
    internal void LoadHotCornerSettings()
    {
        hotCornersLoading = true;
        var settings = owner.Services.Settings;
        HotCorners = settings.HotCornersEnabled;
        HotCornerTopLeftIndex = Index(settings.HotCornerTopLeft); HotCornerTopRightIndex = Index(settings.HotCornerTopRight);
        HotCornerBottomLeftIndex = Index(settings.HotCornerBottomLeft); HotCornerBottomRightIndex = Index(settings.HotCornerBottomRight);
        hotCornersLoading = false;
    }
    partial void OnHotCornersBusyChanged(bool value) => OnPropertyChanged(nameof(HotCornersCanToggle));
    partial void OnHotCornersChanged(bool value)
    {
        if (hotCornersLoading) return;
        if (HotCornersBusy)
        { hotCornersLoading = true; HotCorners = !value; hotCornersLoading = false; return; }
        SaveCornerSettings(); _ = ApplyHotCornersAsync(value);
    }
    partial void OnHotCornerTopLeftIndexChanged(int oldValue, int newValue) => CornerIndexChanged(oldValue, newValue, x => HotCornerTopLeftIndex = x);
    partial void OnHotCornerTopRightIndexChanged(int oldValue, int newValue) => CornerIndexChanged(oldValue, newValue, x => HotCornerTopRightIndex = x);
    partial void OnHotCornerBottomLeftIndexChanged(int oldValue, int newValue) => CornerIndexChanged(oldValue, newValue, x => HotCornerBottomLeftIndex = x);
    partial void OnHotCornerBottomRightIndexChanged(int oldValue, int newValue) => CornerIndexChanged(oldValue, newValue, x => HotCornerBottomRightIndex = x);
    private void CornerIndexChanged(int oldValue, int newValue, Action<int> restore)
    {
        if (hotCornersLoading) return;
        if (HotCornersBusy || newValue < 0 || newValue >= HotCornerActions.Count)
        {
            hotCornersLoading = true; restore(Math.Clamp(oldValue, 0, HotCornerActions.Count - 1)); hotCornersLoading = false; return;
        }
        SaveCornerSettings();
    }
    private void SaveCornerSettings()
    {
        if (hotCornersLoading) return;
        var settings = owner.Services.Settings; var value = CornerConfiguration;
        settings.HotCornersEnabled = value.Enabled;
        settings.HotCornerTopLeft = value.TopLeft; settings.HotCornerTopRight = value.TopRight;
        settings.HotCornerBottomLeft = value.BottomLeft; settings.HotCornerBottomRight = value.BottomRight;
        owner.Services.HotCorners?.UpdateConfiguration(value); owner.Services.Changed();
        owner.Hotkeys.RefreshOtherInput();
    }
    internal async Task ApplyHotCornersAsync(bool enabled)
    {
        if (HotCornersBusy) return;
        HotCornersBusy = true;
        try
        {
            if (owner.Services.HotCorners is { } service) await service.ConfigureAsync(CornerConfiguration with { Enabled = enabled });
        }
        catch (Exception)
        {
            hotCornersLoading = true; HotCorners = false; hotCornersLoading = false;
            SaveCornerSettings(); owner.ShowStatus("Hot corners could not start. Turn them on again to retry.", StatusKind.Warning);
        }
        finally { HotCornersBusy = false; }
    }
}
