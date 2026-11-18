using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using FlowDesk.Core.Capture;
using FlowDesk.Core.Desktops;
using FlowDesk.Core.Focus;
using FlowDesk.Core.Layout;
using FlowDesk.Core.Restore;
using FlowDesk.Core.Storage;
using FlowDesk.Core.Windows;

namespace FlowDesk.App.Services;

// UI owns mutable state. Save workers receive immutable snapshots; a single writer orders writes.
public sealed class AppServices : ObservableObject, IDisposable
{
    // One desktop backend for the whole app: capture/restore and the optional window shortcuts borrow it.
    // Its DLL stays unloaded until LoadAsync reloads it, so startup never depends on the backend.
    public VirtualDesktopService Desktops { get; } = new();
    public Win32WindowSystem Windows { get; }
    public SessionTracker Tracker { get; } = new();
    public WorkspaceCapture Capture { get; }
    public WorkspaceRestorer Restorer { get; }
    public WorkspaceSwitcher Switcher { get; }
    public LayoutApplier Layouts { get; }
    public FocusWatcher Focus { get; }
    public FlowDesk.Core.Contexts.IAudioOutput AudioOutput { get; }
    public FlowDesk.Core.Contexts.IDoNotDisturb DoNotDisturb { get; }
    public JsonStore Store { get; } = new();
    public HotkeyService Hotkeys { get; }
    public QuickLookService? QuickLook { get; set; }
    public HotCornerService? HotCorners { get; set; }
    public FlowDeskData Data { get; private set; } = new();
    public AppSettings Settings { get; private set; } = new();
    public string? RecoveryMessage { get; private set; }
    public event Action<string>? SaveFailed;
    private readonly SemaphoreSlim writer = new(1, 1);
    private CancellationTokenSource? debounce;
    private Task<bool> pending = Task.FromResult(true);
    public bool LastSaveSucceeded { get; private set; } = true;
    private string SettingsPath => Path.Combine(Store.Folder, "app-settings.json");
    private bool disposed;
    private bool transferring;

    public AppServices()
    {
        Windows = new(Desktops);
        Hotkeys = new(Store.Folder, Desktops);
        Hotkeys.TerminalProgram = () => Settings.TerminalProgram;
        Capture = new(Windows, Tracker);
        Restorer = new(Windows, new ShellLauncher(), Tracker, desktops: Desktops);
        Switcher = new(Windows, Tracker, Capture, Restorer);
        Layouts = new(Windows); Focus = new(Windows);
        AudioOutput = new AudioOutputService(LogContextFailure);
        DoNotDisturb = new DoNotDisturbService(LogContextFailure);
    }

    internal void LogContextFailure(Exception ex)
    {
        // No device identifiers, profile contents or exception messages in diagnostics.
        try { Directory.CreateDirectory(Store.Folder); File.AppendAllText(Path.Combine(Store.Folder, "contexts.log"),
            $"{DateTimeOffset.UtcNow:O} {ex.GetType().Name} HRESULT=0x{ex.HResult:X8}{Environment.NewLine}"); }
        catch (Exception) { /* A context diagnostic must never fail the workspace switch. */ }
    }

    /// <summary>Loads the desktop backend off the UI thread before anything captures or restores windows.</summary>
    private Task ReloadDesktopsAsync() => Task.Run(() => Desktops.Reload(AppContext.BaseDirectory, Store.Folder));

    /// <summary>The window (and the app) owns the desktop backend; the borrowed shortcuts service does not.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Desktops.Dispose();
    }
    public async Task LoadAsync()
    {
        var loaded = await Task.Run(() =>
        {
            Windows.GetMonitors(); // Seed shared position-based names before any page builds rows.
            var data = Store.Load(out var recovered);
            AppSettings settings = new();
            string? warning = recovered is null ? null
                : "Your data file was damaged; the last good copy was loaded where available. Check your workspaces.";
            try
            {
                if (File.Exists(SettingsPath))
                    settings = AppSettings.FromJson(File.ReadAllText(SettingsPath));
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            { warning = "App settings could not be loaded. Defaults are in use; your workspaces are unchanged."; }
            return (data, settings, warning);
        });
        Data = loaded.data; Settings = loaded.settings; RecoveryMessage = loaded.warning;
        Settings.DisabledShortcuts = new FlowDesk.Core.Hotkeys.ShortcutPolicy(Settings.DisabledShortcuts).DisabledIds.ToList();
        Settings.ShortcutOverrides = FlowDesk.Core.Hotkeys.ShortcutRebinding.Normalize(Settings.ShortcutOverrides, message =>
        {
            try { File.AppendAllText(Path.Combine(Store.Folder, "shortcut-settings.log"), DateTimeOffset.UtcNow.ToString("O") + " " + message + Environment.NewLine); }
            catch (Exception) { /* diagnostics must never prevent startup */ }
        });
        await Hotkeys.SetBindingsAsync(Settings.DisabledShortcuts, Settings.ShortcutOverrides);
        // Load the shared backend before the first capture/restore. A missing or older DLL is not an
        // error: the service stays available for status text and capture falls back to the COM read.
        await ReloadDesktopsAsync();
    }
    public void Changed(bool immediate = false)
    {
        if (transferring) return;
        debounce?.Cancel(); debounce = new();
        pending = PersistAsync(SnapshotData(), JsonSerializer.Serialize(Settings, JsonStore.Options), immediate, debounce.Token);
    }

    /// <summary>
    /// Persists the current state now (no debounce) and reports whether it reached disk. Autosave uses this so
    /// the "saved" indicator only appears after a successful write; ordinary edits keep their short debounce.
    /// </summary>
    public Task<bool> SaveNowAsync()
    {
        if (transferring) return Task.FromResult(false);
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

    internal sealed record ImportUndo(ImportBackup Backup, FlowDeskData Data, AppSettings Settings);
    internal async Task<ImportUndo> ImportAsync(TransferPlan plan)
    {
        if (transferring) throw new InvalidOperationException("An import is already in progress.");
        // Quiesced caller owns UI state; drain earlier snapshots before capturing the backup.
        if (!await SaveNowAsync()) throw new IOException("Your current data could not be saved. Import was cancelled.");
        transferring = true; debounce?.Cancel();
        await writer.WaitAsync();
        try
        {
            var beforeData = SnapshotData(); var beforeSettings = AppSettings.FromJson(Settings.ToJson());
            var backup = await Task.Run(() => ImportBackup.Create(Store.Folder));
            try
            {
                await Task.Run(async () =>
                {
                    await Store.SaveAsync(plan.Data);
                    ImportBackup.AtomicWrite(SettingsPath, System.Text.Encoding.UTF8.GetBytes(plan.Settings.ToJson()));
                });
            }
            catch
            {
                await Task.Run(backup.Restore);
                throw;
            }
            Data = plan.Data; Settings = plan.Settings;
            return new(backup, beforeData, beforeSettings);
        }
        finally { writer.Release(); transferring = false; }
    }
    internal async Task UndoImportAsync(ImportUndo undo)
    {
        if (transferring) throw new InvalidOperationException("An import is already in progress.");
        transferring = true; debounce?.Cancel();
        await writer.WaitAsync();
        try
        {
            await Task.Run(undo.Backup.Restore);
            Data = undo.Data; Settings = undo.Settings;
        }
        finally { writer.Release(); transferring = false; }
    }

    // Snapshot on the UI thread; the shutdown writer must not need that dispatcher to finish.
    // Windows may terminate us after WM_ENDSESSION, so give atomic persistence a bounded opportunity.
    public void FlushForSessionEnd()
    {
        debounce?.Cancel();
        var data = SnapshotData();
        var settings = JsonSerializer.Serialize(Settings, JsonStore.Options);
        try { Task.Run(() => PersistAsync(data, settings, true, CancellationToken.None)).Wait(TimeSpan.FromSeconds(1)); }
        catch (Exception) { /* Never veto Windows shutdown for a storage failure. */ }
    }
}
