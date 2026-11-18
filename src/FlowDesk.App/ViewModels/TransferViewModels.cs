using FlowDesk.App.Services;

namespace FlowDesk.App.ViewModels;

public partial class AppViewModels
{
    internal bool CanTransfer => !IsBusy && !Settings.WindowShortcutsBusy && !Settings.PaletteBusy && !Settings.HotCornersBusy && !Hotkeys.IsBusy;
    internal void ReloadTransferViews(bool replaced)
    {
        string? active = activeWorkspace?.Model.Id;
        if (replaced)
        {
            foreach (var w in WorkspacesCollection) Services.Tracker.ForgetWorkspace(w.Model.Id);
            Services.Switcher.ClearActive(); AutoSave.Coordinator.Deactivate(); active = null;
        }
        foreach (var removed in WorkspacesCollection.Where(w => !Services.Data.Workspaces.Any(m => m.Id == w.Model.Id)))
            Services.Tracker.ForgetWorkspace(removed.Model.Id);
        WorkspacesCollection.Clear();
        foreach (var w in Services.Data.Workspaces) WorkspacesCollection.Add(new(w, this));
        activeWorkspace = WorkspacesCollection.FirstOrDefault(w => w.Model.Id == active);
        if (activeWorkspace is null) { Services.Switcher.ClearActive(); AutoSave.Coordinator.Deactivate(); }
        Workspaces.UpdateEmpty(); Details.Workspace = null;
        Focus.Load(); Settings.Load(); RefreshInputPages(); RefreshAdvancedPages();
        HasReport = false; Navigate(AppPage.Workspaces);
    }
    internal async Task RestoreUndoActivationAsync()
    {
        if (activeWorkspace?.Model.Id == Services.Settings.ActiveWorkspaceId) return;
        activeWorkspace = null; Services.Switcher.ClearActive(); AutoSave.Coordinator.Deactivate();
        // This is a local backup identity, not an imported active id. Startup's existing resume policy
        // only enumerates/rebaselines; it does not restore, launch, focus or move any window.
        await ResumeActiveWorkspaceAsync();
    }
}

public partial class SettingsViewModel
{
    internal async Task<IReadOnlyList<string>> ApplyTransferredSettingsAsync()
    {
        var warnings = new List<string>(); var services = owner.Services; var s = services.Settings;
        bool wantedShortcuts = s.WindowShortcutsEnabled;
        await ApplyWindowShortcutsAsync(wantedShortcuts);
        if (wantedShortcuts && !s.WindowShortcutsEnabled) warnings.Add("Window shortcuts could not be enabled.");
        warnings.AddRange(await ImportedShortcuts.ApplyAsync(s, bindings =>
            services.Hotkeys.SetBindingsAsync(s.DisabledShortcuts, bindings, s.ClipboardHistoryEnabled)));
        bool wantedQuickLook = s.QuickLookEnabled, wantedCorners = s.HotCornersEnabled;
        await ApplyQuickLookAsync(wantedQuickLook);
        await ApplyHotCornersAsync(wantedCorners);
        if (wantedQuickLook && !s.QuickLookEnabled) warnings.Add("Quick Look could not be enabled.");
        if (wantedCorners && !s.HotCornersEnabled) warnings.Add("Hot corners could not be enabled.");
        try { await services.Hotkeys.SetPaletteWindowsKeyAsync(s.PaletteWindowsKey); }
        catch (Exception) { s.PaletteWindowsKey = false; warnings.Add("Windows-key shortcut could not be enabled."); }
        try { if (ApplyClipboardHistory is not null) await ApplyClipboardHistory(s.ClipboardHistoryEnabled); }
        catch (Exception) { warnings.Add("Clipboard history could not be enabled."); }
        PaletteSourcesChanged?.Invoke(); AiSettingsChanged?.Invoke();
        Load(); owner.RefreshInputPages(); owner.Hotkeys.RefreshStatus();
        return warnings;
    }
}
