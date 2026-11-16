using System.Diagnostics;
using FlowDesk.Core.HotCorners;
using Microsoft.UI.Dispatching;

namespace FlowDesk.App.Services;

public sealed class HotCornerService : IDisposable
{
    private readonly HotkeyService hotkeys;
    private readonly DispatcherQueue dispatcher;
    private readonly Func<Func<bool>, Task<bool>> startFocus;
    private readonly HotCornerMouseState mouse = new();
    private readonly CancellationTokenSource lifetime = new();
    private HotCornerConfiguration configuration = new();
    private int disposed;
    private long contextEpoch;
    private readonly Task worker;
    public HotCornerService(HotkeyService hotkeys, DispatcherQueue dispatcher, Func<Func<bool>, Task<bool>> startFocus)
    {
        this.hotkeys = hotkeys; this.dispatcher = dispatcher; this.startFocus = startFocus;
        worker = Task.Run(LoopAsync);
    }
    public async Task ConfigureAsync(HotCornerConfiguration value)
    {
        if (Volatile.Read(ref disposed) != 0) return;
        Volatile.Write(ref configuration, value);
        try { await hotkeys.SetHotCornersAsync(value.Enabled ? mouse : null); }
        catch
        {
            if (ReferenceEquals(Volatile.Read(ref configuration), value)) Volatile.Write(ref configuration, value with { Enabled = false });
            throw;
        }
    }
    public void UpdateConfiguration(HotCornerConfiguration value)
    {
        if (Volatile.Read(ref disposed) == 0) Volatile.Write(ref configuration, value);
    }
    private async Task LoopAsync()
    {
        var engine = new HotCornerTrigger();
        HotCornerConfiguration? previous = null;
        CornerBounds[] monitors = [];
        nint foreground = 0;
        bool available = false;
        long boundary = -1;
        string? lastBlocked = null;
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(50));
        try
        {
            while (await timer.WaitForNextTickAsync(lifetime.Token))
            {
                try
                {
                    var current = Volatile.Read(ref configuration);
                    var input = mouse.Read();
                    if (!ReferenceEquals(current, previous) || input.HookAvailable != available || input.BoundaryRevision != boundary)
                    { engine.Reset(); Interlocked.Increment(ref contextEpoch); previous = current; available = input.HookAvailable; boundary = input.BoundaryRevision; }
                    if (!current.Enabled || !input.HookAvailable) { engine.Reset(); continue; }
                    var sample = HotCornerNative.Sample(input);
                    if (sample.Blocked != lastBlocked)
                    { Interlocked.Increment(ref contextEpoch); if (sample.Blocked is not null) Trace($"cancel: {sample.Blocked}"); lastBlocked = sample.Blocked; }
                    if (foreground != sample.Foreground || !monitors.SequenceEqual(sample.Monitors))
                    { engine.Reset(); Interlocked.Increment(ref contextEpoch); foreground = sample.Foreground; monitors = sample.Monitors; Trace("cancel: foreground/monitor change"); }
                    var decision = engine.Tick(sample.Point, input.Revision, Environment.TickCount64, monitors, current, sample.Blocked);
                    if (decision.Diagnostic is not null) Trace(decision.Diagnostic);
                    if (decision.Fired is not CornerLocation corner) continue;
                    long firedAt = Environment.TickCount64;
                    long epoch = Volatile.Read(ref contextEpoch);
                    if (decision.Action == HotCornerAction.StartFocus)
                    {
                        dispatcher.TryEnqueue(async () =>
                        {
                            try
                            {
                                if (!StillSafe(current, input.Revision, corner.Point, firedAt, sample.Foreground, epoch)) { Trace("cancel: stale focus action"); return; }
                                bool started = await startFocus(() => StillSafe(current, input.Revision, corner.Point, firedAt, sample.Foreground, epoch));
                                Trace($"action=StartFocus started={started}");
                            }
                            catch (Exception ex) { Trace($"action=StartFocus failed={ex.GetType().Name}"); }
                        });
                    }
                    else if (StillSafe(current, input.Revision, corner.Point, firedAt, sample.Foreground, epoch))
                    {
                        bool success = HotCornerNative.Execute(decision.Action);
                        Trace($"action={decision.Action} success={success}");
                    }
                }
                catch (Exception ex) { engine.Reset(); Interlocked.Increment(ref contextEpoch); Trace($"cancel: sample/action failed={ex.GetType().Name}"); }
            }
        }
        catch (OperationCanceledException) { }
    }
    private bool StillSafe(HotCornerConfiguration expected, long revision, CornerPoint point, long firedAt, nint foreground, long epoch)
    {
        if (!HotCornerTrigger.ActionIsFresh(firedAt, Environment.TickCount64, epoch, Volatile.Read(ref contextEpoch)) || Volatile.Read(ref disposed) != 0 || !expected.Enabled || !ReferenceEquals(Volatile.Read(ref configuration), expected)) return false;
        var input = mouse.Read();
        if (!input.HookAvailable || input.SessionBlocked || input.Revision != revision) return false;
        var sample = HotCornerNative.Sample(input);
        var latest = mouse.Read();
        return Volatile.Read(ref disposed) == 0 && ReferenceEquals(Volatile.Read(ref configuration), expected) &&
            latest.HookAvailable && !latest.SessionBlocked && latest.Revision == revision &&
            HotCornerTrigger.ActionIsFresh(firedAt, Environment.TickCount64, epoch, Volatile.Read(ref contextEpoch)) && sample.Foreground == foreground && sample.Blocked is null && sample.Point == point && HotCornerGeometry.At(point, sample.Monitors) is CornerLocation corner &&
            HotCornerGeometry.Outer(corner, sample.Monitors);
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        Volatile.Write(ref configuration, new()); mouse.SetHookAvailable(false); lifetime.Cancel();
        _ = worker.ContinueWith(_ => lifetime.Dispose(), TaskScheduler.Default);
        // No UI-thread join: a queued Start Focus callback must be allowed to observe disposed.
        // Hook ownership remains with HotkeyService: setting off detaches it; app shutdown calls StopAll/Dispose.
    }
    [Conditional("DEBUG")]
    private static void Trace(string text)
    {
#if DEBUG
        string? path = Environment.GetEnvironmentVariable("FLOWDESK_HOTCORNER_DIAGNOSTICS_LOG");
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) return;
        string line = $"{DateTimeOffset.UtcNow:O} {text}{Environment.NewLine}";
        ThreadPool.QueueUserWorkItem(_ => { try { lock (LogGate) File.AppendAllText(path, line); } catch (Exception) { } });
#endif
    }
#if DEBUG
    private static readonly object LogGate = new();
#endif
}
