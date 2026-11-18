using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlowDesk.Core.Hotkeys;
using FlowDesk.Core.Palette;
using Microsoft.UI.Xaml;

namespace FlowDesk.App.ViewModels;

public partial class SettingsViewModel
{
    [ObservableProperty] public partial bool CommandPalette { get; set; } = true;
    [ObservableProperty] public partial bool PaletteWindowsKey { get; set; }
    [ObservableProperty] public partial bool PaletteBusy { get; set; }
    [ObservableProperty] public partial string PaletteStatus { get; set; } = "";
    [ObservableProperty] public partial bool ClipboardHistory { get; set; }
    [ObservableProperty] public partial string ClipboardStatus { get; set; } = "Off";
    [ObservableProperty] public partial bool PaletteWindowsEnabled { get; set; } = true;
    [ObservableProperty] public partial bool PaletteFilesEnabled { get; set; } = true;
    [ObservableProperty] public partial bool PaletteEdgeBookmarks { get; set; } = true;
    [ObservableProperty] public partial bool PaletteChromeBookmarks { get; set; } = true;
    [ObservableProperty] public partial bool PaletteFirefoxBookmarks { get; set; } = true;
    [ObservableProperty] public partial string PaletteFileBackendStatus { get; set; } = "Checked when the palette opens";
    [ObservableProperty] public partial bool PaletteCalculatorEnabled { get; set; } = true;
    [ObservableProperty] public partial bool PaletteSystemEnabled { get; set; } = true;
    [ObservableProperty] public partial bool PaletteWebEnabled { get; set; } = true;
    [ObservableProperty] public partial bool PaletteWebFallback { get; set; } = true;
    [ObservableProperty] public partial int PaletteWebEngineIndex { get; set; }
    [ObservableProperty] public partial string PaletteWebTemplate { get; set; } = "";
    public Visibility PaletteCustomVisibility => PaletteWebEngineIndex == (int)WebEngine.Custom ? Visibility.Visible : Visibility.Collapsed;
    public string PaletteWebTemplateError => PaletteCustomVisibility == Visibility.Visible && !WebSearch.IsValidTemplate(PaletteWebTemplate)
        ? "Enter an HTTPS URL with %s where the search terms go." : "";
    public Visibility PaletteWebTemplateErrorVisibility => PaletteWebTemplateError.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    internal Action? PaletteSourcesChanged { get; set; }
    partial void OnPaletteWindowsEnabledChanged(bool value) => SavePaletteSources();
    partial void OnPaletteFilesEnabledChanged(bool value) => SavePaletteSources();
    partial void OnPaletteEdgeBookmarksChanged(bool value) => SavePaletteSources();
    partial void OnPaletteChromeBookmarksChanged(bool value) => SavePaletteSources();
    partial void OnPaletteFirefoxBookmarksChanged(bool value) => SavePaletteSources();
    partial void OnPaletteCalculatorEnabledChanged(bool value) => SavePaletteSources();
    partial void OnPaletteSystemEnabledChanged(bool value) => SavePaletteSources();
    partial void OnPaletteWebEnabledChanged(bool value) => SavePaletteSources();
    partial void OnPaletteWebFallbackChanged(bool value) => SavePaletteSources();
    partial void OnPaletteWebEngineIndexChanged(int value)
    {
        OnPropertyChanged(nameof(PaletteCustomVisibility)); OnPropertyChanged(nameof(PaletteWebTemplateError));
        OnPropertyChanged(nameof(PaletteWebTemplateErrorVisibility));
        if (Enum.IsDefined((WebEngine)value)) SavePaletteSources();
    }
    partial void OnPaletteWebTemplateChanged(string value)
    {
        OnPropertyChanged(nameof(PaletteWebTemplateError)); OnPropertyChanged(nameof(PaletteWebTemplateErrorVisibility)); SavePaletteSources();
    }
    private void SavePaletteSources()
    {
        if (loading || paletteLoading || !Enum.IsDefined((WebEngine)PaletteWebEngineIndex)) return;
        var s = owner.Services.Settings;
        s.PaletteWindowsEnabled = PaletteWindowsEnabled;
        s.PaletteFilesEnabled = PaletteFilesEnabled;
        s.PaletteEdgeBookmarks = PaletteEdgeBookmarks;
        s.PaletteChromeBookmarks = PaletteChromeBookmarks;
        s.PaletteFirefoxBookmarks = PaletteFirefoxBookmarks;
        s.PaletteCalculatorEnabled = PaletteCalculatorEnabled;
        s.PaletteSystemEnabled = PaletteSystemEnabled;
        s.PaletteWebEnabled = PaletteWebEnabled;
        s.PaletteWebFallback = PaletteWebFallback;
        s.PaletteWebEngine = (WebEngine)PaletteWebEngineIndex;
        s.PaletteWebTemplate = PaletteWebTemplate;
        owner.Services.Changed(); PaletteSourcesChanged?.Invoke();
    }
    internal Func<bool, Task>? ApplyClipboardHistory { get; set; }
    internal Action? ClearClipboardHistory { get; set; }
    internal Func<Task>? ClearRunHistory { get; set; }
    [RelayCommand] private async Task ClearPaletteRunHistoryAsync() { if (ClearRunHistory is not null) await ClearRunHistory(); }
    [RelayCommand] private void ClearClipboard() => ClearClipboardHistory?.Invoke();
    public bool PaletteCanToggle => !PaletteBusy && !owner.Hotkeys.IsBusy;
    public bool PaletteWindowsKeyCanToggle => PaletteCanToggle && (PaletteWindowsKey || owner.Services.Hotkeys.PaletteWindowsKeyAvailable);
    public string PaletteWindowsKeyStatus => owner.Services.Hotkeys.PaletteWindowsKeyAvailable ? "" :
        "Windows-key option unavailable. The regular palette shortcut still works.";
    public Visibility PaletteWindowsKeyStatusVisibility => PaletteWindowsKeyStatus.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    public string PaletteShortcut => owner.Services.Settings.EffectiveShortcut(HotkeyCatalog.PaletteId).Shortcut;
    public string PaletteDescription => $"Search workspaces, apps and actions with {PaletteShortcut}. Works independently of window shortcuts.";
    private bool paletteLoading;
    partial void OnPaletteBusyChanged(bool value)
    { OnPropertyChanged(nameof(PaletteCanToggle)); OnPropertyChanged(nameof(PaletteWindowsKeyCanToggle)); }
    internal void RefreshPaletteSettings()
    {
        paletteLoading = true;
        CommandPalette = owner.Services.Settings.CommandPaletteEnabled;
        PaletteWindowsKey = owner.Services.Settings.PaletteWindowsKey;
        ClipboardHistory = owner.Services.Settings.ClipboardHistoryEnabled;
        var s = owner.Services.Settings;
        PaletteWindowsEnabled = s.PaletteWindowsEnabled;
        PaletteFilesEnabled = s.PaletteFilesEnabled;
        PaletteEdgeBookmarks = s.PaletteEdgeBookmarks;
        PaletteChromeBookmarks = s.PaletteChromeBookmarks;
        PaletteFirefoxBookmarks = s.PaletteFirefoxBookmarks;
        PaletteCalculatorEnabled = s.PaletteCalculatorEnabled;
        PaletteSystemEnabled = s.PaletteSystemEnabled;
        PaletteWebEnabled = s.PaletteWebEnabled;
        PaletteWebFallback = s.PaletteWebFallback;
        PaletteWebEngineIndex = Enum.IsDefined(s.PaletteWebEngine) ? (int)s.PaletteWebEngine : 0;
        PaletteWebTemplate = s.PaletteWebTemplate ?? "";
        RefreshCustomCommands();
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
        OnPropertyChanged(nameof(PaletteWindowsKeyCanToggle));
        OnPropertyChanged(nameof(PaletteWindowsKeyStatus));
        OnPropertyChanged(nameof(PaletteWindowsKeyStatusVisibility));
        OnPropertyChanged(nameof(PaletteShortcut)); OnPropertyChanged(nameof(PaletteDescription));
    }
    partial void OnClipboardHistoryChanged(bool value)
    {
        if (loading || paletteLoading) return;
        if (!PaletteCanToggle || ApplyClipboardHistory is null) { RefreshPaletteSettings(); return; }
        _ = ApplyClipboardAsync(value);
    }
    partial void OnPaletteWindowsKeyChanged(bool value)
    {
        if (loading || paletteLoading) return;
        if (!PaletteCanToggle) { RefreshPaletteSettings(); return; }
        _ = ApplyWindowsKeyAsync(value);
    }
    private async Task ApplyWindowsKeyAsync(bool value)
    {
        PaletteBusy = true;
        try
        {
            await owner.Services.Hotkeys.SetPaletteWindowsKeyAsync(value);
            owner.Services.Settings.PaletteWindowsKey = value;
            owner.Services.Changed();
        }
        catch { owner.ShowStatus("Windows-key shortcut is unavailable. Your previous choice was kept.", StatusKind.Warning); }
        finally { PaletteBusy = false; RefreshPaletteSettings(); }
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
