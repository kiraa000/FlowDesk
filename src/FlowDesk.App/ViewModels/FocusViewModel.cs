using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlowDesk.Core.Focus;
using FlowDesk.Core.Model;
using FlowDesk.Core.Storage;
using FlowDesk.App.Services;
using FlowDesk.Core.Windows;

namespace FlowDesk.App.ViewModels;

public sealed record FocusRuleRow(FocusAppRule Rule, string ExePath)
{
    public override string ToString() => Name;
    public string Name => Rule.Name;
    public string Detail => Rule.ExeName is { Length: > 0 } exe ? exe + ".exe" : "Packaged app";
}

// UI state is dispatcher-owned. Even watcher state reads run on a worker: its lock covers enumeration.
public partial class FocusViewModel : ObservableObject, IContextFocus
{
    private readonly AppViewModels owner;
    private readonly SemaphoreSlim operations = new(1, 1);
    private bool loading;
    private double totalSeconds;
    public ObservableCollection<FocusRuleRow> Blocked { get; } = [];
    public ObservableCollection<FocusRuleRow> Allowed { get; } = [];
    public bool BlockedEmpty => Blocked.Count == 0;
    public bool AllowedEmpty => Allowed.Count == 0;
    public int[] Durations { get; } = [25, 50, 90];
    [ObservableProperty] public partial int Duration { get; set; } = 25;
    [ObservableProperty] public partial int StrictnessIndex { get; set; } = 1;
    [ObservableProperty] public partial bool BlockAll { get; set; }
    [ObservableProperty] public partial bool IsRunning { get; set; }
    [ObservableProperty] public partial bool CanPause { get; set; }
    [ObservableProperty] public partial string PauseLabel { get; set; } = "Pause";
    [ObservableProperty] public partial string RemainingText { get; set; } = "25:00";
    [ObservableProperty] public partial double Progress { get; set; }
    [ObservableProperty] public partial string SessionDescription { get; set; } = "";
    public bool IsIdle => !IsRunning;
    public bool CanStart => UxDecisions.CanStartFocus(IsRunning, BlockAll, Blocked.Count);
    public string StartDisabledReason => IsRunning ? "" : CanStart ? "" : "Add a blocked app or turn on Block every app.";
    public event Action? SessionChanged;
    private WindowInfo? lastBlockedWindow;
    private int allowanceGeneration;
    [ObservableProperty] public partial string LastBlockedApp { get; set; } = "";
    [ObservableProperty] public partial bool CanAllowLastBlocked { get; set; }
    public FocusViewModel(AppViewModels owner)
    {
        this.owner = owner;
        Blocked.CollectionChanged += (_, _) => UpdateStart();
        SessionChanged += ClearLastBlocked;
        owner.Services.Focus.AppBlocked += (_, e) =>
        {
            int generation = Volatile.Read(ref allowanceGeneration);
            owner.Ui.Enqueue(() =>
            {
                if (generation != allowanceGeneration) return;
                // Available even when Windows notifications are disabled or silently fail.
                lastBlockedWindow = e.CanAllowTemporarily ? e.Window : null;
                LastBlockedApp = e.AppName;
                CanAllowLastBlocked = e.CanAllowTemporarily;
            });
        };
    }
    private void ClearLastBlocked()
    {
        Interlocked.Increment(ref allowanceGeneration);
        lastBlockedWindow = null;
        LastBlockedApp = "";
        CanAllowLastBlocked = false;
    }
    [RelayCommand]
    private async Task AllowLastBlockedAsync()
    {
        var window = lastBlockedWindow;
        ClearLastBlocked();
        int generation = allowanceGeneration;
        await operations.WaitAsync();
        try
        {
            bool allowed = generation == allowanceGeneration && window is not null &&
                await Task.Run(() => owner.Services.Focus.AllowTemporarily(window));
            owner.ShowStatus(allowed ? "App allowed for 5 minutes. Open it again to continue." : "That allowance is no longer available.");
        }
        catch (Exception ex) { owner.ShowStatus("The app could not be allowed: " + ex.Message, StatusKind.Warning); }
        finally { operations.Release(); }
    }
    internal void Load()
    {
        loading = true; var s = owner.Services.Data.Focus;
        Blocked.Clear(); Allowed.Clear();
        Duration = FocusSettings.DurationChoices.Contains(s.DurationMinutes) ? s.DurationMinutes : 25;
        StrictnessIndex = Math.Clamp((int)s.Strictness, 0, 2); BlockAll = s.BlockAllExceptAllowed;
        foreach (var r in s.Blocked) Blocked.Add(new(r, owner.Services.Settings.FocusIconPaths.GetValueOrDefault(RuleKey(r), "")));
        foreach (var r in s.Allowed) Allowed.Add(new(r, owner.Services.Settings.FocusIconPaths.GetValueOrDefault(RuleKey(r), "")));
        loading = false; UpdateEmpty();
    }
    partial void OnDurationChanged(int value) => Persist();
    partial void OnStrictnessIndexChanged(int value) => Persist();
    partial void OnBlockAllChanged(bool value) { Persist(); UpdateStart(); }
    partial void OnIsRunningChanged(bool value) { OnPropertyChanged(nameof(IsIdle)); UpdateStart(); }
    private void UpdateStart()
    {
        OnPropertyChanged(nameof(CanStart)); OnPropertyChanged(nameof(StartDisabledReason));
        StartCommand.NotifyCanExecuteChanged();
    }
    private void Persist()
    {
        if (loading) return;
        owner.Services.Data.Focus = new() { DurationMinutes = Duration,
            Strictness = (FocusStrictness)Math.Clamp(StrictnessIndex, 0, 2), BlockAllExceptAllowed = BlockAll,
            Blocked = Blocked.Select(r => r.Rule).ToList(), Allowed = Allowed.Select(r => r.Rule).ToList() };
        owner.Services.Changed();
    }
    private void UpdateEmpty() { OnPropertyChanged(nameof(BlockedEmpty)); OnPropertyChanged(nameof(AllowedEmpty)); }
    private static string RuleKey(FocusAppRule rule) => !string.IsNullOrEmpty(rule.Aumid)
        ? "aumid:" + rule.Aumid.ToLowerInvariant() : "exe:" + rule.ExeName?.ToLowerInvariant();
    [RelayCommand] private async Task RefreshIconsAsync()
    {
        if (Blocked.Count + Allowed.Count == 0) return;
        try
        {
            var windows = await Task.Run(() => owner.Services.Windows.GetAppWindows());
            bool changed = false;
            foreach (var list in new[] { Blocked, Allowed })
                for (int i = 0; i < list.Count; i++)
                {
                    var row = list[i];
                    var path = windows.FirstOrDefault(w => row.Rule.Matches(w.ExePath, w.Aumid))?.ExePath;
                    if (string.IsNullOrEmpty(path) || path == row.ExePath) continue;
                    list[i] = row with { ExePath = path };
                    owner.Services.Settings.FocusIconPaths[RuleKey(row.Rule)] = path; changed = true;
                }
            if (changed) owner.Services.Changed();
        }
        catch (Exception) { /* Labelled fallback icons remain usable when Shell access is unavailable. */ }
    }
    [RelayCommand] private async Task AddRunningAsync(string list) => await owner.ExecuteAsync(async () =>
    {
        var candidates = await Task.Run(() => owner.Services.Capture.CaptureOpenWindows());
        var apps = candidates.Where(c => c.Problem is null && c.Item.App is not null).DistinctBy(c => c.Window.AppKey).ToArray();
        if (apps.Length == 0) { owner.ShowStatus("No running apps are available. Browse for an executable instead."); return; }
        var selected = await owner.Ui.PickRunningAsync(apps);
        if (selected is null) return;
        Add(list, new() { Name = selected.Item.Name, ExeName = selected.Window.ExeName, Aumid = selected.Window.Aumid }, selected.Window.ExePath);
    });
    [RelayCommand] private async Task BrowseAsync(string list) => await owner.ExecuteAsync(async () =>
    {
        var path = await owner.Ui.PickPathAsync(ItemKind.App);
        if (path is not null) Add(list, new() { Name = Path.GetFileNameWithoutExtension(path), ExeName = Path.GetFileNameWithoutExtension(path).ToLowerInvariant() }, path);
    });
    private void Add(string list, FocusAppRule rule, string path)
    {
        if (IsRunning) return;
        var target = list == "Allowed" ? Allowed : Blocked;
        if (target.Any(r => string.Equals(r.Rule.ExeName, rule.ExeName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(r.Rule.Aumid, rule.Aumid, StringComparison.OrdinalIgnoreCase))) return;
        target.Add(new(rule, path)); owner.Services.Settings.FocusIconPaths[RuleKey(rule)] = path;
        UpdateEmpty(); Persist();
    }
    [RelayCommand] private void RemoveBlocked(FocusRuleRow row) { if (!IsRunning) { Blocked.Remove(row); UpdateEmpty(); Persist(); } }
    [RelayCommand] private void RemoveAllowed(FocusRuleRow row) { if (!IsRunning) { Allowed.Remove(row); UpdateEmpty(); Persist(); } }
    [RelayCommand(CanExecute = nameof(CanStart))] private async Task StartAsync() => await owner.ExecuteAsync(async () =>
    {
        await operations.WaitAsync();
        try
        {
            if (!CanStart) return;
            Persist();
            var snapshot = JsonSerializer.Deserialize<FocusSettings>(JsonSerializer.Serialize(owner.Services.Data.Focus, JsonStore.Options), JsonStore.Options)!;
            totalSeconds = snapshot.DurationMinutes * 60;
            SessionChanged?.Invoke();
            await Task.Run(() => owner.Services.Focus.Start(snapshot));
            await RefreshAsync();
        }
        finally { operations.Release(); }
    });
    internal async Task<bool> TryStartFromHotCornerAsync(Func<bool> stillAllowed)
    {
        if (owner.IsBusy || !stillAllowed()) return false;
        bool started = false;
        await owner.ExecuteAsync(async () =>
        {
            await operations.WaitAsync();
            try
            {
                if (!stillAllowed()) return;
                if (await Task.Run(() => owner.Services.Focus.State is not null)) return;
                if (!stillAllowed()) return;
                Persist();
                var snapshot = JsonSerializer.Deserialize<FocusSettings>(JsonSerializer.Serialize(owner.Services.Data.Focus, JsonStore.Options), JsonStore.Options)!;
                totalSeconds = snapshot.DurationMinutes * 60;
                SessionChanged?.Invoke();
                bool accepted = await Task.Run(() =>
                {
                    if (!stillAllowed() || owner.Services.Focus.State is not null) return false;
                    owner.Services.Focus.Start(snapshot); return true;
                });
                if (!accepted) return;
                await RefreshAsync(); started = true;
            }
            finally { operations.Release(); }
        });
        return started;
    }
    [RelayCommand] private async Task PauseAsync()
    {
        await operations.WaitAsync();
        try
        {
            await Task.Run(() => { if (owner.Services.Focus.State?.Paused == true) owner.Services.Focus.Resume(); else owner.Services.Focus.Pause(); });
            await RefreshAsync();
        }
        finally { operations.Release(); }
    }
    [RelayCommand] private Task EndAsync() => EndSessionAsync();
    public async Task<bool> EndSessionAsync(bool confirmAnyRunning = false)
    {
        await operations.WaitAsync();
        try
        {
            var state = await Task.Run(() => owner.Services.Focus.State);
            if (UxDecisions.ConfirmFocusEnd(state is not null, state?.Strictness == FocusStrictness.Strict, confirmAnyRunning)
                && !await owner.Ui.ConfirmAsync(confirmAnyRunning ? "Exit FlowDesk and end focus?" : "End strict focus?",
                    "This ends your session early. Blocked apps will be available again.",
                    confirmAnyRunning ? "Exit" : "End session")) return false;
            await Task.Run(() => owner.Services.Focus.Stop(force: true));
            await RefreshAsync(); SessionChanged?.Invoke(); return true;
        }
        finally { operations.Release(); }
    }
    public async Task RefreshAsync()
    {
        var sample = await Task.Run(() => (state: owner.Services.Focus.State, remaining: owner.Services.Focus.Remaining));
        if (!IsRunning && sample.state is { } started) totalSeconds = (started.EndsAt - started.StartedAt).TotalSeconds;
        IsRunning = sample.state is not null; CanPause = IsRunning && sample.state?.Strictness != FocusStrictness.Strict;
        if (sample.state is null || sample.state.Strictness != FocusStrictness.Normal) ClearLastBlocked();
        PauseLabel = sample.state?.Paused == true ? "Resume" : "Pause";
        SessionDescription = sample.state?.Paused == true ? "Session paused. Apps are not being blocked."
            : sample.state?.Strictness == FocusStrictness.Gentle ? "Gentle reminders are active. Apps stay open. Closing this window keeps the session running in the tray."
            : "Blocked apps are closed politely. Closing this window keeps the session running in the tray.";
        var seconds = Math.Max(0, (int)Math.Ceiling(sample.remaining.TotalSeconds));
        RemainingText = $"{seconds / 60:00}:{seconds % 60:00}";
        Progress = totalSeconds > 0 ? Math.Clamp(100 * (1 - sample.remaining.TotalSeconds / totalSeconds), 0, 100) : 0;
    }

    public async Task<ContextFocusState> ReadContextStateAsync()
    {
        var state = await Task.Run(() => owner.Services.Focus.State);
        return new(state is not null, state?.Strictness == FocusStrictness.Strict,
            UxDecisions.CanStartFocus(false, BlockAll, Blocked.Count));
    }

    public async Task<FlowDesk.Core.Contexts.ContextNote?> ApplyContextFocusAsync(WorkspaceContext context)
    {
        await operations.WaitAsync();
        try
        {
            var current = await ReadContextStateAsync();
            var step = FlowDesk.Core.Contexts.ContextPlanner.Plan(context, new(current.Running, current.Strict,
                current.CanStart, new(false, null), new(false, [], null)))
                .FirstOrDefault(s => s.Kind is FlowDesk.Core.Contexts.ContextStepKind.FocusEnd or FlowDesk.Core.Contexts.ContextStepKind.FocusStart);
            if (step is null) return null;
            if (step.SkipReason is { } reason) return new(reason);
            if (context.Focus == ContextFocus.End)
            {
                bool stopped = await Task.Run(() => owner.Services.Focus.Stop()); // never force a Strict session
                await RefreshAsync(); SessionChanged?.Invoke();
                return new(stopped ? "Focus session ended" : "Strict focus session kept");
            }
            // Copy the Focus page lists, but do not persist the context's duration/strictness as page defaults.
            var snapshot = JsonSerializer.Deserialize<FocusSettings>(JsonSerializer.Serialize(owner.Services.Data.Focus, JsonStore.Options), JsonStore.Options)!;
            snapshot.DurationMinutes = context.FocusMinutes;
            snapshot.Strictness = Enum.IsDefined(context.FocusStrictness) ? context.FocusStrictness : FocusStrictness.Normal;
            totalSeconds = snapshot.DurationMinutes * 60;
            SessionChanged?.Invoke();
            await Task.Run(() => owner.Services.Focus.Start(snapshot));
            await RefreshAsync();
            return new($"Focus {snapshot.DurationMinutes} min");
        }
        finally { operations.Release(); }
    }
}
