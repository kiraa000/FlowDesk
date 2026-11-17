using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlowDesk.Core.Hotkeys;

namespace FlowDesk.App.ViewModels;

public partial class SettingsViewModel
{
    [ObservableProperty] public partial bool CommandPalette { get; set; } = true;
    [ObservableProperty] public partial bool PaletteBusy { get; set; }
    [ObservableProperty] public partial string PaletteStatus { get; set; } = "";
    [ObservableProperty] public partial bool ClipboardHistory { get; set; }
    [ObservableProperty] public partial string ClipboardStatus { get; set; } = "Off";
    internal Func<bool, Task>? ApplyClipboardHistory { get; set; }
    internal Action? ClearClipboardHistory { get; set; }
    [RelayCommand] private void ClearClipboard() => ClearClipboardHistory?.Invoke();
    public bool PaletteCanToggle => !PaletteBusy && !owner.Hotkeys.IsBusy;
    public string PaletteShortcut => owner.Services.Settings.EffectiveShortcut(HotkeyCatalog.PaletteId).Shortcut;
    public string PaletteDescription => $"Search workspaces, apps and actions with {PaletteShortcut}. Works independently of window shortcuts.";
    private bool paletteLoading;
    partial void OnPaletteBusyChanged(bool value) => OnPropertyChanged(nameof(PaletteCanToggle));
    internal void RefreshPaletteSettings()
    {
        paletteLoading = true;
        CommandPalette = owner.Services.Settings.CommandPaletteEnabled;
        ClipboardHistory = owner.Services.Settings.ClipboardHistoryEnabled;
        paletteLoading = false;
        var runtime = owner.Services.Hotkeys.ShortcutRuntime;
        string clipboardShortcut = owner.Services.Settings.EffectiveShortcut(HotkeyCatalog.ClipboardId).Shortcut;
        ClipboardStatus = !ClipboardHistory ? "Off · Nothing is captured" : runtime.Registration.Conflicts.Contains(HotkeyCatalog.ClipboardId)
            ? $"{clipboardShortcut} is in use by another app." : runtime.Registration.Registered.Contains(HotkeyCatalog.ClipboardId)
            ? $"{clipboardShortcut} · Memory only" : $"{clipboardShortcut} · Shortcut not available";
        PaletteStatus = !CommandPalette ? "Off" : runtime.Registration.Conflicts.Contains(HotkeyCatalog.PaletteId)
            ? $"{PaletteShortcut} is in use by another app." : runtime.Registration.Registered.Contains(HotkeyCatalog.PaletteId)
            ? $"{PaletteShortcut} · Ready" : $"{PaletteShortcut} · Not available";
        OnPropertyChanged(nameof(PaletteCanToggle));
        OnPropertyChanged(nameof(PaletteShortcut)); OnPropertyChanged(nameof(PaletteDescription));
    }
    partial void OnClipboardHistoryChanged(bool value)
    {
        if (loading || paletteLoading) return;
        if (!PaletteCanToggle || ApplyClipboardHistory is null) { RefreshPaletteSettings(); return; }
        _ = ApplyClipboardAsync(value);
    }
    private async Task ApplyClipboardAsync(bool value)
    {
        PaletteBusy = true;
        try { await ApplyClipboardHistory!(value); owner.Services.Changed(); }
        catch { owner.ShowStatus("Clipboard history could not be changed. Try again.", StatusKind.Warning); }
        finally { PaletteBusy = false; owner.Hotkeys.RefreshStatus(); }
    }
    partial void OnCommandPaletteChanged(bool value)
    {
        if (loading || paletteLoading) return;
        if (!PaletteCanToggle) { RefreshPaletteSettings(); return; }
        _ = ApplyPaletteAsync(value);
    }
    private async Task ApplyPaletteAsync(bool value)
    {
        var previous = owner.Services.Settings.DisabledShortcuts.ToList();
        owner.Services.Settings.CommandPaletteEnabled = value;
        PaletteBusy = true;
        try
        {
            await owner.Services.Hotkeys.SetDisabledShortcutsAsync(owner.Services.Settings.DisabledShortcuts);
            owner.Services.Changed();
        }
        catch (Exception)
        {
            owner.Services.Settings.DisabledShortcuts = previous;
            owner.ShowStatus("Command palette shortcut could not be changed. Your previous choice was kept.", StatusKind.Warning);
        }
        finally { PaletteBusy = false; RefreshPaletteSettings(); owner.Hotkeys.RefreshStatus(); }
    }
}
