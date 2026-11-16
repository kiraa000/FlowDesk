using CommunityToolkit.Mvvm.ComponentModel;
using FlowDesk.Core.Hotkeys;

namespace FlowDesk.App.ViewModels;

public partial class SettingsViewModel
{
    [ObservableProperty] public partial bool CommandPalette { get; set; } = true;
    [ObservableProperty] public partial bool PaletteBusy { get; set; }
    [ObservableProperty] public partial string PaletteStatus { get; set; } = "";
    public bool PaletteCanToggle => !PaletteBusy && !owner.Hotkeys.IsBusy;
    public string PaletteShortcut => HotkeyCatalog.Find(HotkeyCatalog.PaletteId)?.Shortcut ?? "";
    public string PaletteDescription => $"Search workspaces, apps and actions with {PaletteShortcut}. Works independently of window shortcuts.";
    private bool paletteLoading;
    partial void OnPaletteBusyChanged(bool value) => OnPropertyChanged(nameof(PaletteCanToggle));
    internal void RefreshPaletteSettings()
    {
        paletteLoading = true;
        CommandPalette = owner.Services.Settings.CommandPaletteEnabled;
        paletteLoading = false;
        var runtime = owner.Services.Hotkeys.ShortcutRuntime;
        PaletteStatus = !CommandPalette ? "Off" : runtime.Registration.Conflicts.Contains(HotkeyCatalog.PaletteId)
            ? $"{PaletteShortcut} is in use by another app." : runtime.Registration.Registered.Contains(HotkeyCatalog.PaletteId)
            ? $"{PaletteShortcut} · Ready" : $"{PaletteShortcut} · Not available";
        OnPropertyChanged(nameof(PaletteCanToggle));
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
