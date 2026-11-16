using FlowDesk.Core.Desktops;
using FlowDesk.Core.Hotkeys;
using FlowDesk.Core.QuickLook;
using FlowDesk.Core.HotCorners;

namespace FlowDesk.App.Services;

/// <summary>
/// Lifetime owner for the optional window shortcuts: the VirtualDesktopAccessor backend and the
/// <see cref="HotkeyHost"/>. UI code calls Start/Stop/Reload; notification messages go through the
/// app's normal toast path. One gate serializes every transition.
/// </summary>
public sealed class HotkeyService(string dataDirectory, VirtualDesktopService? shared = null) : IDisposable
{
    // Deliberately never disposed: a transition queued behind the gate may still be waiting when
    // the app closes, and SemaphoreSlim would throw if it were disposed under a waiter. The
    // instance is released with the process, and every transition re-checks _disposed under the gate.
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly VirtualDesktopService? _shared = shared;
    // Borrowed when shared; otherwise loaded and owned by this service (standalone tests).
    private IVirtualDesktops? _desktops = shared;
    private bool _ownsDesktops;
    private HotkeyHost? _host;
    private bool _disposed;
    private int _disposeStarted;
    private readonly ShortcutTerminal _terminal = new();
    private bool _shortcutsRequested;
    private IQuickLookInput? _quickLook;
    private IHotCornerInput? _hotCorners;
    private IReadOnlyList<string> _disabledShortcuts = [];
    public Func<string?>? TerminalProgram { get; set; }

    /// <summary>Toast callback; set by the window once notifications are initialized.</summary>
    public Action<string>? Notify { get; set; }

    public bool IsRunning => _shortcutsRequested && _host?.IsRunning == true;
    internal bool ShortcutsRequested => _shortcutsRequested;
    public IReadOnlyList<string> Conflicts => _host?.Conflicts ?? [];
    public ShortcutRuntime ShortcutRuntime
    {
        get
        {
            var host = Volatile.Read(ref _host);
            return new(_shortcutsRequested && host?.IsRunning == true, DesktopsAvailable, host?.WheelHookAvailable == true,
                host?.KeyboardHookAvailable == true, host?.BindingStatus ?? ShortcutRegistrationSnapshot.Empty, host?.IsPaused == true);
        }
    }
    public async Task SetDisabledShortcutsAsync(IEnumerable<string>? ids)
    {
        var normalized = new ShortcutPolicy(ids).DisabledIds;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed) return;
            var previous = _disabledShortcuts;
            _disabledShortcuts = normalized;
            try { if (_host?.IsRunning == true) await _host.ConfigureBindingsAsync(normalized).ConfigureAwait(false); }
            catch
            {
                _disabledShortcuts = previous;
                try { if (_host?.IsRunning == true) await _host.ConfigureBindingsAsync(previous).ConfigureAwait(false); }
                catch (Exception) { Notify?.Invoke("Some shortcuts could not be restored. Turn window shortcuts off and on to retry."); }
                throw;
            }
        }
        finally { _gate.Release(); }
    }
    public bool DesktopsAvailable => _desktops?.IsAvailable == true;
    public bool WheelAvailable => _host?.WheelHookAvailable == true;
    public bool WinWheelAvailable => _host?.WinWheelHookAvailable == true;
    public string DesktopStatus => _desktops?.Status
        ?? "Desktop shortcuts need VirtualDesktopAccessor.dll. Choose the file to turn them on.";

    public async Task StartAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed) return;
            if (_shortcutsRequested && _host?.IsRunning == true) return;
            try { await RequestWindowShortcutsAsync(async () =>
            {
                await StopCoreAsync().ConfigureAwait(false);
                await StartCoreAsync().ConfigureAwait(false);
            }).ConfigureAwait(false); }
            catch
            {
                // Restore independently enabled modules without the failed shortcut request.
                if (_quickLook is not null || _hotCorners is not null)
                {
                    try { await StartCoreAsync().ConfigureAwait(false); }
                    catch (Exception) { Notify?.Invoke("Input modules could not be restored. Turn them off and on in Settings to retry."); }
                }
                throw;
            }
        }
        finally { _gate.Release(); }
    }

    public async Task StopAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed) return;
            _shortcutsRequested = false;
            await StopCoreAsync().ConfigureAwait(false);
            if (_quickLook is not null || _hotCorners is not null) await StartCoreAsync().ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    /// <summary>Caller holds _gate. Failed startup must not leave a latent request that a later
    /// independent module toggle can accidentally resurrect while Settings says shortcuts are off.</summary>
    internal async Task RequestWindowShortcutsAsync(Func<Task> transition)
    {
        _shortcutsRequested = true;
        try { await transition().ConfigureAwait(false); }
        catch { _shortcutsRequested = false; throw; }
    }

    public async Task SetQuickLookAsync(IQuickLookInput? input)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed) return;
            if (_shortcutsRequested && _host?.IsRunning == true)
            {
                if (input is not null && !_host.KeyboardHookAvailable)
                    throw new InvalidOperationException("Quick Look keyboard hook is unavailable.");
                _quickLook = input;
                _host.SetQuickLook(input);
                return; // do not disturb shortcut pause state or in-flight actions
            }
            var previous = _quickLook;
            _quickLook = input;
            await StopCoreAsync().ConfigureAwait(false);
            try
            {
                if (_shortcutsRequested || _quickLook is not null || _hotCorners is not null) await StartCoreAsync().ConfigureAwait(false);
            }
            catch
            {
                // A failed module must not silently disable the unrelated window shortcuts.
                _quickLook = previous;
                if (_shortcutsRequested || _quickLook is not null || _hotCorners is not null) await StartCoreAsync().ConfigureAwait(false);
                throw;
            }
        }
        finally { _gate.Release(); }
    }

    /// <summary>Changes the mouse module in place; existing shortcut state and Quick Look keep their host.</summary>
    public async Task SetHotCornersAsync(IHotCornerInput? input)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed) return;
            if (_host?.IsRunning == true)
            {
                await _host.ConfigureHotCornersAsync(input).ConfigureAwait(false);
                _hotCorners = input;
                if (input is null && !_shortcutsRequested && _quickLook is null) await StopCoreAsync().ConfigureAwait(false);
                return;
            }
            var previous = _hotCorners; _hotCorners = input;
            try { if (input is not null) await StartCoreAsync().ConfigureAwait(false); }
            catch { _hotCorners = previous; throw; }
        }
        finally { _gate.Release(); }
    }

    /// <summary>App shutdown: do not restart a host for another enabled module.</summary>
    public async Task StopAllAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed) return;
            _shortcutsRequested = false; _quickLook = null; _hotCorners = null;
            await StopCoreAsync().ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    /// <summary>Stops, reloads the DLL from its candidate paths, and starts again if it was running.</summary>
    public async Task ReloadBackendAsync()
        => await ReloadAsync(null).ConfigureAwait(false);

    private async Task ReloadAsync(HotkeyHost? source)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed) return;
            // A queued hotkey reload must not resurrect a host after Settings turned it off,
            // nor replace a newer generation. No worker waits for its own Stop/Join.
            if (source is not null && !ReferenceEquals(_host, source)) return;
            bool wasRunning = _host?.IsRunning == true;
            await StopCoreAsync().ConfigureAwait(false);
            if (_shared is not null)
            {
                // Reload the shared backend in place: other callers (capture/restore) keep the same instance.
                await Task.Run(() => _shared.Reload(AppContext.BaseDirectory, dataDirectory)).ConfigureAwait(false);
            }
            else
            {
                _desktops?.Dispose();
                _desktops = null; _ownsDesktops = false;
                await Task.Run(EnsureBackend).ConfigureAwait(false);
            }
            if (wasRunning) await StartCoreAsync().ConfigureAwait(false);
            if (source is not null) Notify?.Invoke("Window shortcuts reloaded");
        }
        finally { _gate.Release(); }
    }

    private async Task StartCoreAsync()
    {
        if (_host is { IsRunning: true }) return;
        if (_host is not null) await StopCoreAsync().ConfigureAwait(false);
        if (_desktops is null) await Task.Run(EnsureBackend).ConfigureAwait(false);
        HotkeyHost? host = null;
        host = new HotkeyHost(new Win32WindowOps(), _desktops, Notify,
            () => _terminal.Open(TerminalProgram?.Invoke(), Notify),
            () => RequestReload(host!), _shortcutsRequested, _quickLook, _hotCorners, _disabledShortcuts);
        try
        {
            await host.StartAsync().ConfigureAwait(false);
            _host = host;
        }
        catch
        {
            host.Dispose();
            throw;
        }
    }

    internal Task ReloadFromHostAsync(HotkeyHost source) => ReloadAsync(source);

    private void RequestReload(HotkeyHost source)
    {
        _ = Task.Run(async () =>
        {
            try { await ReloadFromHostAsync(source).ConfigureAwait(false); }
            catch (Exception) { Notify?.Invoke("Window shortcuts could not be reloaded. Turn them off and on in Settings to try again."); }
        });
    }

    private async Task StopCoreAsync()
    {
        if (_host is null) return;
        try { await _host.StopAsync().ConfigureAwait(false); }
        finally
        {
            _host.Dispose();
            _host = null;
        }
    }

    private void EnsureBackend()
    {
        if (_desktops is not null) return;
        _desktops = VirtualDesktopAccessor.Load(
            VirtualDesktopAccessor.CandidatePaths(AppContext.BaseDirectory, dataDirectory));
        _ownsDesktops = true;
    }

    /// <summary>
    /// Stops everything on first call; later calls are no-ops. The flag is set while holding the
    /// gate, so a Start/Reload that was queued behind us sees it and cannot resurrect the host.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0) return;
        _gate.Wait();
        try
        {
            if (_disposed) return;
            _disposed = true;
            try { StopCoreAsync().GetAwaiter().GetResult(); } catch (Exception) { }
            // Never dispose or clear the shared backend: the window owns it and capture/restore still use it.
            if (_ownsDesktops)
            {
                try { _desktops?.Dispose(); } catch (Exception) { }
                _desktops = null; _ownsDesktops = false;
            }
        }
        finally { _gate.Release(); }
    }
}
