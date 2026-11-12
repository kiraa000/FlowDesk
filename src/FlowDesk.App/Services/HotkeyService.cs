using FlowDesk.Core.Desktops;
using FlowDesk.Core.Hotkeys;

namespace FlowDesk.App.Services;

/// <summary>
/// Lifetime owner for the optional window shortcuts: the VirtualDesktopAccessor backend and the
/// <see cref="HotkeyHost"/>. UI code calls Start/Stop/Reload; notification messages go through the
/// app's normal toast path. One gate serializes every transition.
/// </summary>
public sealed class HotkeyService(string dataDirectory) : IDisposable
{
    // Deliberately never disposed: a transition queued behind the gate may still be waiting when
    // the app closes, and SemaphoreSlim would throw if it were disposed under a waiter. The
    // instance is released with the process, and every transition re-checks _disposed under the gate.
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IVirtualDesktops? _desktops;
    private HotkeyHost? _host;
    private bool _disposed;
    private int _disposeStarted;

    /// <summary>Toast callback; set by the window once notifications are initialized.</summary>
    public Action<string>? Notify { get; set; }

    public bool IsRunning => _host?.IsRunning == true;
    public IReadOnlyList<string> Conflicts => _host?.Conflicts ?? [];
    public bool DesktopsAvailable => _desktops?.IsAvailable == true;
    public bool WheelAvailable => _host?.WheelHookAvailable == true;
    public string DesktopStatus => _desktops?.Status
        ?? "Desktop shortcuts need VirtualDesktopAccessor.dll. Choose the file to turn them on.";

    public async Task StartAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed) return;
            await StartCoreAsync().ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task StopAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed) return;
            await StopCoreAsync().ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    /// <summary>Stops, reloads the DLL from its candidate paths, and starts again if it was running.</summary>
    public async Task ReloadBackendAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed) return;
            bool wasRunning = IsRunning;
            await StopCoreAsync().ConfigureAwait(false);
            _desktops?.Dispose();
            _desktops = null;
            await Task.Run(EnsureBackend).ConfigureAwait(false);
            if (wasRunning) await StartCoreAsync().ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    private async Task StartCoreAsync()
    {
        if (_host is { IsRunning: true }) return;
        if (_host is not null) await StopCoreAsync().ConfigureAwait(false);
        if (_desktops is null) await Task.Run(EnsureBackend).ConfigureAwait(false);
        var host = new HotkeyHost(new Win32WindowOps(), _desktops, Notify);
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

    private void EnsureBackend() =>
        _desktops ??= VirtualDesktopAccessor.Load(
            VirtualDesktopAccessor.CandidatePaths(AppContext.BaseDirectory, dataDirectory));

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
            try { _desktops?.Dispose(); } catch (Exception) { }
            _desktops = null;
        }
        finally { _gate.Release(); }
    }
}
