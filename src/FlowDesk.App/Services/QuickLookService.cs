using System.Diagnostics;
using FlowDesk.App.Views;
using FlowDesk.Core.QuickLook;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace FlowDesk.App.Services;

/// <summary>UI-thread owner. Hook callbacks only read atomic snapshots and enqueue. Polling is serialized
/// (100 ms after each bounded STA read), so an unresponsive Explorer cannot build up requests.</summary>
public sealed class QuickLookService : IQuickLookInput, IDisposable
{
    private readonly DispatcherQueue dispatcher;
    private readonly AppServices services;
    private readonly QuickLookShell shell = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private QuickLookWindow? window;
    private CancellationTokenSource? session;
    private QuickLookItem? item;
    private nint owner;
    private volatile bool enabled;
    private bool disposed, polling, opening;
    private int? desktop;
    private long enableGeneration;
    private readonly QuickLookPathSession pathSession = new();
    public bool Enabled => enabled;
    public bool ShowPath(string path, nint source)
    {
        if (disposed || !enabled || QuickLookNative.GetForegroundWindow() != source) return false;
        Close();
        if (!pathSession.Begin(path, source, QuickLookNative.GetForegroundWindow(), enabled)) return false;
        try
        {
            window ??= CreateWindow(); owner = source; session = new();
            desktop = services.Desktops.GetCurrent();
            if (desktop is int target) services.Desktops.MoveWindow((long)window.NativeHandle, target);
            item = new(path, System.IO.Path.GetFileName(path), path, false, System.IO.Path.GetExtension(path));
            window.ShowFrame(source, services.Settings.Theme);
            _ = window.DisplayAsync(item); timer.Start(); return true;
        }
        catch (Exception) { Close(); return false; }
    }
    public bool ClosePathPreview(nint source)
    {
        if (!pathSession.BelongsTo(source)) return false;
        Close(); return true;
    }
    public nint PreviewOwner => Volatile.Read(ref owner);
    public QuickLookService(DispatcherQueue dispatcher, AppServices services)
    {
        this.dispatcher = dispatcher; this.services = services;
        timer.Tick += async (_, _) => await PollAsync();
    }
    public async Task SetEnabledAsync(bool value)
    {
        if (disposed) return;
        long generation = ++enableGeneration;
        enabled = value;
        if (!value) Close();
        try { await services.Hotkeys.SetQuickLookAsync(value ? this : null); }
        catch { if (generation == enableGeneration) { enabled = false; Close(); } throw; }
    }
    public void Post(QuickLookCommand command, nint source) => dispatcher.TryEnqueue(() =>
    {
        if (!enabled || disposed || QuickLookNative.GetForegroundWindow() != source) return;
        if (command == QuickLookCommand.Close) Close();
        else if (command == QuickLookCommand.Open) Open();
        else if (command == QuickLookCommand.Toggle)
        {
            if (owner != 0) Close();
            else
            {
                try
                {
                    window ??= CreateWindow();
                    owner = source; item = null; session = new();
                    desktop = services.Desktops.GetCurrent();
                    // A reusable hidden window can still belong to the desktop of its previous
                    // preview. Move ONLY our own window, without activating/switching any desktop.
                    if (desktop is int target) services.Desktops.MoveWindow((long)window.NativeHandle, target);
                    timer.Start(); _ = PollAsync();
                }
                catch (Exception) { Close(); }
            }
        }
    });
    private QuickLookWindow CreateWindow()
    {
        var result = new QuickLookWindow(shell);
        result.Dismissed += Close; result.OpenRequested += Open;
        return result;
    }
    private async Task PollAsync()
    {
        if (owner == 0 || disposed) return;
        nint source = owner;
        var activeSession = session!;
        var token = activeSession.Token;
        if (!QuickLookNative.IsWindow(source) || QuickLookNative.GetForegroundWindow() != source ||
            (desktop is not null && services.Desktops.GetCurrent() is int current && current != desktop))
        { Close(); return; }
        if (pathSession.Active) return; // explicit path stays pinned; no Explorer polling
        if (polling) return;
        polling = true;
        try
        {
            var selected = await shell.ReadAsync(source, token);
            token.ThrowIfCancellationRequested();
            // Focus can change while COM is running. Never publish one stale frame after that change.
            if (!ReferenceEquals(session, activeSession) || QuickLookNative.GetForegroundWindow() != source)
            { if (ReferenceEquals(session, activeSession)) Close(); return; }
            if (selected is null)
            {
                // Shell timeout/busy: never show an empty initial frame or open a stale item.
                if (item is null) Close();
                return;
            }
            if (selected == QuickLookItem.Empty) { Close(); return; }
            if (selected.Identity == item?.Identity) return;
            if (item is null) window!.ShowFrame(source, services.Settings.Theme);
            item = selected;
            _ = window!.DisplayAsync(selected);
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (ReferenceEquals(session, activeSession)) Close(); }
        finally { polling = false; }
    }
    private void Open() => _ = OpenAsync();
    private async Task OpenAsync()
    {
        if (opening || owner == 0 || session is null) return;
        nint source = owner;
        var activeSession = session;
        var token = activeSession.Token;
        QuickLookItem? selected = null;
        opening = true;
        try
        {
            // Enter can arrive just after an arrow and before the next poll. Resolve the selection
            // again instead of accidentally launching the old displayed file. A thumbnail may own
            // the STA briefly; retry only within this one bounded request, never launch stale state.
            var watch = Stopwatch.StartNew();
            if (pathSession.Active) selected = item;
            while (!pathSession.Active && watch.ElapsedMilliseconds < 700)
            {
                selected = await shell.ReadAsync(source, token);
                if (selected is not null) break;
                await Task.Delay(25, token);
            }
            token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(session, activeSession) || QuickLookNative.GetForegroundWindow() != source) return;
        }
        catch (OperationCanceledException) { return; }
        catch (Exception) { return; }
        finally { opening = false; }
        string? path = selected?.Path;
        string? virtualItem = selected?.Identity;
        Close();
        if (path is null && virtualItem?.StartsWith("::{", StringComparison.Ordinal) != true) return;
        // ShellExecute via ProcessStartInfo preserves associations; never pass file text to a shell command.
        try
        {
            if (path is not null) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            else
            {
                var start = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
                start.ArgumentList.Add("shell:" + virtualItem); Process.Start(start);
            }
        }
        catch (Exception) { /* default app may be missing; never take down the tray app */ }
    }
    private void Close()
    {
        pathSession.Clear();
        owner = 0; timer.Stop(); session?.Cancel(); session?.Dispose(); session = null;
        item = null; window?.HidePreview();
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; enabled = false; Close(); window?.Shutdown(); shell.Dispose();
    }
    public static string DetectConflicts()
    {
        var tools = new List<string>();
        foreach (var entry in new[] { ("QuickLook", "QuickLook also uses Space"), ("PowerToys.Peek.UI", "PowerToys Peek uses Ctrl+Space by default") })
        {
            try
            {
                var processes = Process.GetProcessesByName(entry.Item1);
                if (processes.Length > 0) tools.Add(entry.Item2);
                foreach (var process in processes) process.Dispose();
            }
            catch (Exception) { }
        }
        return tools.Count == 0 ? "" : string.Join(". ", tools) + ". FlowDesk takes Space only in Explorer and on the desktop.";
    }
}
