using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlowDesk.Core.Hotkeys;
using FlowDesk.Core.HotCorners;

namespace FlowDesk.App.ViewModels;

public sealed record ShortcutKeyCap(string Key, string Join);
public sealed record HotkeyGroupViewModel(string Title, IReadOnlyList<HotkeyRowViewModel> Rows);
public sealed partial class HotkeyRowViewModel : ObservableObject
{
    private readonly HotkeysViewModel owner;
    public HotkeyBinding Binding { get; }
    public string Label => Binding.Label;
    public string AutomationName => Label + " shortcut";
    public IReadOnlyList<ShortcutKeyCap> Keys { get; }
    [ObservableProperty] public partial bool IsOn { get; set; } = true;
    [ObservableProperty] public partial bool CanToggle { get; set; }
    [ObservableProperty] public partial string StatusText { get; set; } = "Not available";
    private bool applying;
    public HotkeyRowViewModel(HotkeyBinding binding, HotkeysViewModel owner)
    {
        Binding = binding; this.owner = owner;
        var parts = binding.Shortcut.Split('+');
        Keys = parts.Select((key, index) => new ShortcutKeyCap(key, index == parts.Length - 1 ? "" : "+")).ToArray();
    }
    partial void OnIsOnChanged(bool value) { if (!applying) owner.Change(this, value); }
    internal void Apply(bool enabled, bool canToggle, ShortcutStatus status)
    {
        applying = true;
        try { IsOn = enabled; CanToggle = canToggle; StatusText = ShortcutPresentation.StatusText(status); }
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
    public HotkeysViewModel(AppViewModels owner)
    {
        this.owner = owner;
        rows = HotkeyCatalog.All.Select(b => new HotkeyRowViewModel(b, this)).ToArray();
        RebuildGroups();
    }
    partial void OnFilterChanged(string value) => RebuildGroups();
    partial void OnIsBusyChanged(bool value)
    {
        RefreshCommand.NotifyCanExecuteChanged(); TurnAllOnCommand.NotifyCanExecuteChanged(); RefreshStatus();
    }
    private bool CanApply => !IsBusy && !owner.Settings.PaletteBusy && owner.Services.Settings.WindowShortcutsEnabled && !owner.Settings.WindowShortcutsBusy;
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
        foreach (var row in rows) row.Apply(policy.Enabled(row.Binding.Id), CanApply, ShortcutPresentation.Status(row.Binding, settings.WindowShortcutsEnabled, policy, runtime));
        int enabled = rows.Count(r => policy.Enabled(r.Binding.Id));
        int conflicts = rows.Count(r => ShortcutPresentation.Status(r.Binding, settings.WindowShortcutsEnabled, policy, runtime) == ShortcutStatus.Conflict);
        Summary = $"{enabled} of {rows.Count} on · {conflicts} {(conflicts == 1 ? "conflict" : "conflicts")}" + (runtime.Paused ? " · paused" : "");
        RefreshCommand.NotifyCanExecuteChanged(); TurnAllOnCommand.NotifyCanExecuteChanged();
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
        if (!CanApply) { RefreshStatus(); return; }
        var ids = new HashSet<string>(new ShortcutPolicy(owner.Services.Settings.DisabledShortcuts).DisabledIds, StringComparer.Ordinal);
        if (enabled) ids.Remove(row.Binding.Id); else ids.Add(row.Binding.Id);
        _ = ApplyAsync(ids, persist: true);
    }
    [RelayCommand(CanExecute = nameof(CanApply))]
    private Task RefreshAsync() => ApplyAsync(owner.Services.Settings.DisabledShortcuts, persist: false);
    [RelayCommand(CanExecute = nameof(CanApply))]
    private Task TurnAllOnAsync() => ApplyAsync([], persist: true);
    [RelayCommand] private void OpenSettings() => owner.Navigate(AppPage.Settings);
    private async Task ApplyAsync(IEnumerable<string>? disabledIds, bool persist)
    {
        if (!CanApply) return;
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
