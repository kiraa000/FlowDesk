using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlowDesk.Core;
using FlowDesk.Core.Hotkeys;
using FlowDesk.Core.HotCorners;

namespace FlowDesk.App.ViewModels;

public sealed record ShortcutKeyCap(string Key, string Join);
public sealed record HotkeyGroupViewModel(string Title, IReadOnlyList<HotkeyRowViewModel> Rows);
public sealed partial class HotkeyRowViewModel : ObservableObject
{
    private readonly HotkeysViewModel owner;
    public HotkeyBinding Binding { get; private set; }
    public string Label => Binding.Label;
    public string AutomationName => Label + " shortcut";
    public IReadOnlyList<ShortcutKeyCap> Keys { get; private set; } = [];
    public bool CanEdit => !Binding.IsMouseWheel && !owner.IsBusy;
    public string EditHint => Binding.IsMouseWheel ? "Mouse-wheel gestures use fixed modifiers and cannot be reassigned." : "Change this shortcut";
    public string EditAutomationName => "Edit " + Label + " shortcut";
    public bool IsCustom => KeyChord.From(Binding) != KeyChord.From(HotkeyCatalog.Find(Binding.Id)!);
    internal void UpdateBinding(HotkeyBinding binding)
    {
        Binding = binding;
        var parts = binding.Shortcut.Split('+');
        Keys = parts.Select((key, index) => new ShortcutKeyCap(key, index == parts.Length - 1 ? "" : "+")).ToArray();
        OnPropertyChanged(nameof(Binding)); OnPropertyChanged(nameof(Keys)); OnPropertyChanged(nameof(IsCustom)); OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(Label)); OnPropertyChanged(nameof(AutomationName)); OnPropertyChanged(nameof(EditAutomationName));
    }
    [ObservableProperty] public partial bool IsOn { get; set; } = true;
    [ObservableProperty] public partial bool CanToggle { get; set; }
    [ObservableProperty] public partial string StatusText { get; set; } = "Not available";
    private ShortcutStatus status = ShortcutStatus.Unavailable;
    public bool ShowStatus => ShortcutPresentation.ShowStatusDetail(status);
    public bool IsConflict => ShortcutPresentation.IsConflict(status);
    public bool IsNeutralStatus => ShowStatus && !IsConflict;
    private bool applying;
    public HotkeyRowViewModel(HotkeyBinding binding, HotkeysViewModel owner)
    {
        Binding = binding; this.owner = owner;
        UpdateBinding(binding);
    }
    partial void OnIsOnChanged(bool value) { if (!applying) owner.Change(this, value); }
    internal void Apply(bool enabled, bool canToggle, ShortcutStatus status)
    {
        applying = true;
        try
        {
            IsOn = enabled; CanToggle = canToggle;
            StatusText = ShortcutPresentation.StatusText(status);
            if (this.status != status)
            {
                this.status = status;
                OnPropertyChanged(nameof(ShowStatus)); OnPropertyChanged(nameof(IsConflict)); OnPropertyChanged(nameof(IsNeutralStatus));
            }
        }
        finally { applying = false; }
    }
}

public sealed partial class HotkeysViewModel : ObservableObject
{
    private readonly AppViewModels owner;
    private readonly IReadOnlyList<HotkeyRowViewModel> rows;
    public ObservableCollection<HotkeyGroupViewModel> Groups { get; } = [];
    [ObservableProperty] public partial string Filter { get; set; } = "";
    [ObservableProperty] public partial string Summary { get; set; } = "";
    [ObservableProperty] public partial bool HasNoMatches { get; set; }
    [ObservableProperty] public partial bool IsBusy { get; set; }
    [ObservableProperty] public partial string QuickLookSummary { get; set; } = "";
    [ObservableProperty] public partial string HotCornersSummary { get; set; } = "";
    public bool GameMode
    {
        get => owner.Services.Hotkeys.GameMode;
        set { if (value != GameMode) _ = SetGameModeAsync(value); }
    }
    private async Task SetGameModeAsync(bool enabled)
    {
        try { await owner.Services.Hotkeys.SetGameModeAsync(enabled); }
        catch (Exception) { owner.ShowStatus("Game mode could not be changed. Your previous state was kept.", StatusKind.Warning); }
        RefreshStatus();
    }
    public bool AutoGameMode
    {
        get => owner.Services.Settings.AutoGameMode;
        set { owner.Services.Settings.AutoGameMode = value; owner.Services.Changed(); OnPropertyChanged(); }
    }
    public HotkeysViewModel(AppViewModels owner)
    {
        this.owner = owner;
        rows = HotkeyCatalog.All.Select(b => new HotkeyRowViewModel(b, this)).ToArray();
        RebuildGroups();
    }
    partial void OnFilterChanged(string value) => RebuildGroups();
    partial void OnIsBusyChanged(bool value)
    {
        RefreshCommand.NotifyCanExecuteChanged(); TurnAllOnCommand.NotifyCanExecuteChanged(); TurnAllOffCommand.NotifyCanExecuteChanged(); RefreshStatus();
    }
    private bool CanEditBindings => !IsBusy && !owner.Settings.PaletteBusy && !owner.Settings.WindowShortcutsBusy;
    private bool CanApply => CanEditBindings && owner.Services.Settings.WindowShortcutsEnabled;
    private void RebuildGroups()
    {
        Groups.Clear();
        foreach (var group in ShortcutPresentation.Groups)
        {
            var matches = rows.Where(r => ShortcutPresentation.Group(r.Binding) == group.Group && ShortcutPresentation.Matches(r.Binding, Filter)).ToArray();
            if (matches.Length > 0) Groups.Add(new(group.Title, matches));
        }
        HasNoMatches = Groups.Count == 0;
    }
    internal void RefreshStatus()
    {
        var settings = owner.Services.Settings;
        var policy = new ShortcutPolicy(settings.DisabledShortcuts);
        var runtime = owner.Services.Hotkeys.ShortcutRuntime;
        var effective = ShortcutRebinding.Effective(settings.ShortcutOverrides).ToDictionary(b => b.Id);
        foreach (var row in rows)
        {
            row.UpdateBinding(effective[row.Binding.Id]);
            bool clipboard = row.Binding.Id == HotkeyCatalog.ClipboardId;
            bool module = GameModeRules.Retained(row.Binding.Id) || (clipboard ? settings.ClipboardHistoryEnabled : settings.WindowShortcutsEnabled);
            var moduleRuntime = runtime with { Running = GameModeRules.Retained(row.Binding.Id) || clipboard || runtime.Running };
            bool canToggle = CanEditBindings && module && (!clipboard || settings.ClipboardHistoryEnabled);
            row.Apply(policy.Enabled(row.Binding.Id) && (!clipboard || settings.ClipboardHistoryEnabled), canToggle, ShortcutPresentation.Status(row.Binding, module, policy, moduleRuntime));
        }
        int enabled = rows.Count(r => r.IsOn);
        int conflicts = rows.Count(r => r.IsConflict);
        Summary = $"{enabled} of {rows.Count} on · {CountText.Format(conflicts, "conflict")}" + (runtime.GameMode ? " · game mode" : runtime.Paused ? " · paused" : "");
        RefreshCommand.NotifyCanExecuteChanged(); TurnAllOnCommand.NotifyCanExecuteChanged(); TurnAllOffCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(GameMode)); OnPropertyChanged(nameof(AutoGameMode));
        RefreshOtherInput();
        owner.Settings.RefreshPaletteSettings();
    }
    internal void RefreshOtherInput()
    {
        var settings = owner.Services.Settings;
        QuickLookSummary = $"{(settings.QuickLookEnabled ? "On" : "Off")} · Space in Explorer and on the desktop";
        string Label(HotCornerAction action) => HotCornerConfiguration.Actions.FirstOrDefault(x => x.Action == action)?.Label ?? "None";
        HotCornersSummary = $"{(settings.HotCornersEnabled ? "On" : "Off")} · Top-left: {Label(settings.HotCornerTopLeft)} · Top-right: {Label(settings.HotCornerTopRight)} · Bottom-left: {Label(settings.HotCornerBottomLeft)} · Bottom-right: {Label(settings.HotCornerBottomRight)}";
    }
    internal void Change(HotkeyRowViewModel row, bool enabled)
    {
        bool independent = GameModeRules.Retained(row.Binding.Id) || row.Binding.Id == HotkeyCatalog.ClipboardId;
        if (!CanEditBindings || (!independent && !owner.Services.Settings.WindowShortcutsEnabled) ||
            (row.Binding.Id == HotkeyCatalog.ClipboardId && !owner.Services.Settings.ClipboardHistoryEnabled)) { RefreshStatus(); return; }
        var ids = new HashSet<string>(new ShortcutPolicy(owner.Services.Settings.DisabledShortcuts).DisabledIds, StringComparer.Ordinal);
        if (enabled) ids.Remove(row.Binding.Id); else ids.Add(row.Binding.Id);
        _ = ApplyAsync(ids, persist: true, independent: independent);
    }
    [RelayCommand(CanExecute = nameof(CanApply))]
    private Task RefreshAsync() => ApplyAsync(owner.Services.Settings.DisabledShortcuts, persist: false);
    [RelayCommand(CanExecute = nameof(CanApply))]
    private Task TurnAllOnAsync() => ApplyAsync([], persist: true);
    [RelayCommand(CanExecute = nameof(CanEditBindings))]
    private Task TurnAllOffAsync() => ApplyAsync(GameModeRules.TurnAllOffIds, persist: true, independent: true);
    [RelayCommand] private void OpenSettings() => owner.Navigate(AppPage.Settings);
    public async Task<string?> SaveEditAsync(string id, KeyChord candidate, ShortcutResolution resolution)
    {
        if (!CanEditBindings) return "Another shortcut change is in progress.";
        var settings = owner.Services.Settings;
        try
        {
            var fixedCheck = CheckCandidate(id, candidate);
            if (fixedCheck.Blocked) return fixedCheck.Error ?? "This shortcut is reserved by Windows.";
            var plan = ShortcutRebinding.Plan(settings.ShortcutOverrides, settings.DisabledShortcuts, id, candidate, resolution);
            return await SavePlanAsync(plan);
        }
        catch (Exception ex) { return ex.Message; }
    }
    public ShortcutConflictResult CheckCandidate(string id, KeyChord candidate)
    {
        var settings = owner.Services.Settings;
        var actions = new[] { settings.HotCornerTopLeft, settings.HotCornerTopRight, settings.HotCornerBottomLeft, settings.HotCornerBottomRight };
        return ShortcutRebinding.Evaluate(ShortcutRebinding.Effective(settings.ShortcutOverrides), id, candidate, settings.QuickLookEnabled,
            settings.HotCornersEnabled && actions.Contains(HotCornerAction.ShowDesktop), settings.HotCornersEnabled && actions.Contains(HotCornerAction.TaskView));
    }
    public Task<string?> ResetAllAsync() => SavePlanAsync(new(new(), new ShortcutPolicy(owner.Services.Settings.DisabledShortcuts).DisabledIds));
    private async Task<string?> SavePlanAsync(ShortcutEditPlan plan)
    {
        if (!CanEditBindings) return "Another shortcut change is in progress.";
        IsBusy = true;
        try
        {
            await owner.Services.Hotkeys.SetBindingsAsync(plan.DisabledIds, plan.Overrides);
            owner.Services.Settings.ShortcutOverrides = plan.Overrides;
            owner.Services.Settings.DisabledShortcuts = plan.DisabledIds.ToList();
            owner.Services.Changed();
            return null;
        }
        catch (Exception ex) { return ex.Message; }
        finally { IsBusy = false; RefreshStatus(); owner.Settings.RefreshWindowShortcutsState(); }
    }
    private async Task ApplyAsync(IEnumerable<string>? disabledIds, bool persist, bool independent = false)
    {
        if (!CanEditBindings || (!independent && !owner.Services.Settings.WindowShortcutsEnabled)) return;
        var previous = new ShortcutPolicy(owner.Services.Settings.DisabledShortcuts).DisabledIds.ToList();
        var next = new ShortcutPolicy(disabledIds).DisabledIds.ToList();
        // Publish desired state before disabling controls; the native acknowledgement follows.
        owner.Services.Settings.DisabledShortcuts = next;
        IsBusy = true;
        try
        {
            await owner.Services.Hotkeys.SetDisabledShortcutsAsync(next);
            if (persist) owner.Services.Changed();
        }
        catch (Exception)
        {
            owner.Services.Settings.DisabledShortcuts = previous;
            if (persist) owner.Services.Changed();
            owner.ShowStatus("Shortcut changes could not be applied. Your previous choices were kept.", StatusKind.Warning);
        }
        finally
        {
            IsBusy = false; owner.Settings.RefreshWindowShortcutsState(); RefreshStatus();
        }
    }
}
