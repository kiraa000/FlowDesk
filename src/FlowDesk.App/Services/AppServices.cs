using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using FlowDesk.Core.Capture;
using FlowDesk.Core.Focus;
using FlowDesk.Core.Layout;
using FlowDesk.Core.Restore;
using FlowDesk.Core.Storage;
using FlowDesk.Core.Windows;

namespace FlowDesk.App.Services;

// UI owns mutable state. Save workers receive immutable snapshots; a single writer orders writes.
public sealed class AppServices : ObservableObject
{
    public Win32WindowSystem Windows { get; } = new();
    public SessionTracker Tracker { get; } = new();
    public WorkspaceCapture Capture { get; }
    public WorkspaceRestorer Restorer { get; }
    public WorkspaceSwitcher Switcher { get; }
    public LayoutApplier Layouts { get; }
    public FocusWatcher Focus { get; }
    public JsonStore Store { get; } = new();
    public FlowDeskData Data { get; private set; } = new();
    public AppSettings Settings { get; private set; } = new();
    public string? RecoveryMessage { get; private set; }
    public event Action<string>? SaveFailed;
    private readonly SemaphoreSlim writer = new(1, 1);
    private CancellationTokenSource? debounce;
    private Task<bool> pending = Task.FromResult(true);
    public bool LastSaveSucceeded { get; private set; } = true;
    private string SettingsPath => Path.Combine(Store.Folder, "app-settings.json");

    public AppServices()
    {
        Capture = new(Windows, Tracker);
        Restorer = new(Windows, new ShellLauncher(), Tracker);
        Switcher = new(Windows, Tracker, Capture, Restorer);
        Layouts = new(Windows); Focus = new(Windows);
    }
    public async Task LoadAsync()
    {
        var loaded = await Task.Run(() =>
        {
            var data = Store.Load(out var recovered);
            AppSettings settings = new();
            string? warning = recovered is null ? null
                : "Your data file was damaged; the last good copy was loaded where available. Check your workspaces.";
            try
            {
                if (File.Exists(SettingsPath))
                    settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new();
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            { warning = "App settings could not be loaded. Defaults are in use; your workspaces are unchanged."; }
            return (data, settings, warning);
        });
        Data = loaded.data; Settings = loaded.settings; RecoveryMessage = loaded.warning;
    }
    public void Changed(bool immediate = false)
    {
        debounce?.Cancel(); debounce = new();
        pending = PersistAsync(SnapshotData(), JsonSerializer.Serialize(Settings, JsonStore.Options), immediate, debounce.Token);
    }

    /// <summary>
    /// Persists the current state now (no debounce) and reports whether it reached disk. Autosave uses this so
    /// the "saved" indicator only appears after a successful write; ordinary edits keep their short debounce.
    /// </summary>
    public Task<bool> SaveNowAsync()
    {
        // Drop a pending debounced write (its snapshot is older) but never cancel a save already in flight:
        // a later Changed write queues behind it on the same writer, so the newest snapshot still wins.
        debounce?.Cancel(); debounce = null;
        return PersistAsync(SnapshotData(), JsonSerializer.Serialize(Settings, JsonStore.Options), immediate: true, CancellationToken.None);
    }

    private FlowDeskData SnapshotData() =>
        JsonSerializer.Deserialize<FlowDeskData>(JsonSerializer.Serialize(Data, JsonStore.Options), JsonStore.Options)!;

    private async Task<bool> PersistAsync(FlowDeskData snapshot, string settings, bool immediate, CancellationToken token)
    {
        try
        {
            if (!immediate) await Task.Delay(500, token);
            await writer.WaitAsync(token);
            try
            {
                token.ThrowIfCancellationRequested();
                await Task.Run(async () =>
                {
                    await Store.SaveAsync(snapshot);
                    var tmp = SettingsPath + ".tmp";
                    await File.WriteAllTextAsync(tmp, settings);
                    if (File.Exists(SettingsPath)) File.Replace(tmp, SettingsPath, SettingsPath + ".bak", true);
                    else File.Move(tmp, SettingsPath);
                });
                LastSaveSucceeded = true;
                return true;
            }
            finally { writer.Release(); }
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex)
        {
            bool wasSucceeding = LastSaveSucceeded;
            LastSaveSucceeded = false;
            if (wasSucceeding)
                SaveFailed?.Invoke("Changes could not be saved. Check access to the data folder and try again. " + ex.Message);
            return false;
        }
    }
    public async Task FlushAsync() { Changed(true); await pending; }
}
