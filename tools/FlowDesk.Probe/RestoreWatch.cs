// restore-watch: one real restore of an all-Chrome captured workspace, then a bounded, read-only
// diagnostic watch of the captured Chrome profile's windows. It never enforces a size, never rebinds.
using System.Diagnostics;
using System.Runtime.InteropServices;
using FlowDesk.Core.Model;
using FlowDesk.Core.Restore;
using FlowDesk.Core.Windows;
using static FlowDesk.Probe.RestoreWatchPlan;

namespace FlowDesk.Probe;

internal static class RestoreWatch
{
    private const int PollMs = 250;
    private const int MinSeconds = 1;
    private const int MaxSeconds = 60;

    public static void PrintUsage()
    {
        Console.WriteLine("restore-watch usage:");
        Console.WriteLine("  restore-watch <workspace.json> <seconds>    (whole seconds, 1..60)");
        Console.WriteLine("  Restores the workspace once, then polls every 250 ms for captured Chrome windows only.");
        Console.WriteLine("  Requires EVERY item to be a Chrome App/Website item with a non-empty captured");
        Console.WriteLine("  --user-data-dir; refuses AUMIDs, other apps/browsers, File/Folder items and defaults.");
        Console.WriteLine("  Logs every Place attempt, the actual window right after it, and late changes/closures/replacements.");
        Console.WriteLine("  Never prints command lines, page URLs, titles, or unrelated personal windows.");
    }

    public static async Task<int> RunAsync(Workspace workspace, string? secondsText)
    {
        if (!RestoreWatchPlan.TryCreate(workspace, out var plan, out string? error))
        {
            Console.Error.WriteLine("restore-watch: " + error);
            PrintUsage();
            return 2;
        }
        if (!int.TryParse(secondsText, out int seconds) || seconds < MinSeconds || seconds > MaxSeconds)
        {
            Console.Error.WriteLine($"restore-watch: duration must be a whole number {MinSeconds}..{MaxSeconds} seconds");
            PrintUsage();
            return 2;
        }

        PrintHeader(plan, seconds);
        var real = new Win32WindowSystem();
        Win32WindowSystem.EnsurePerMonitorDpiAwareness();
        var minTrackProbe = new MinTrackProbe();
        var log = new LoggingWindowSystem(real, plan.IsWatched, Console.WriteLine, minTrackProbe);
        var tracker = new SessionTracker();
        var restorer = new WorkspaceRestorer(log, new ShellLauncher(), tracker);
        var report = await restorer.RestoreAsync(workspace).ConfigureAwait(false);

        Console.WriteLine($"{Stamp()} RESTORE         restored {report.Placed}/{report.Items.Count} in {report.Elapsed.TotalSeconds:0.0}s");
        var capturedIds = workspace.Items.Where(plan.IsCapturedChromeItem).Select(i => i.Id).ToHashSet();
        foreach (var r in report.Items)
        {
            // Non-browser details can contain file paths (personal metadata); only show captured-browser detail.
            string detail = capturedIds.Contains(r.ItemId) && !string.IsNullOrEmpty(r.Detail) ? " - " + r.Detail : "";
            Console.WriteLine($"{Stamp()} RESULT          {r.Outcome,-16} item={r.ItemId}{detail}");
        }

        // Track the handles the restorer actually bound, plus the profile's current windows.
        var originals = new List<OriginalWatch>();
        foreach (var item in workspace.Items.Where(plan.IsCapturedChromeItem))
        {
            var live = tracker.Live(real, workspace.Id, item.Id);
            if (live is null || !plan.IsWatched(live))
            {
                var outcome = report.Items.FirstOrDefault(r => r.ItemId == item.Id)?.Outcome;
                Console.WriteLine($"{Stamp()} BOUND           item={item.Id}: no live original handle (outcome {outcome?.ToString() ?? "?"}); not rebinding");
                continue;
            }
            originals.Add(new OriginalWatch(item.Id, Snap(live), item.Window?.Rect));
        }

        var known = new Dictionary<long, WindowSnapshot>();
        foreach (var w in real.GetAppWindows().Where(plan.IsWatched)) known[w.Handle] = Snap(w);

        var originalHandles = originals.Select(o => o.Snapshot.Handle).ToHashSet();
        Console.WriteLine($"{Stamp()} WATCH           baseline capturedChromeWindows={known.Count} boundOriginals={originals.Count} poll={PollMs}ms duration={seconds}s");
        foreach (var t in originals)
            Console.WriteLine($"{Stamp()} BOUND-ORIGINAL  item={t.Id} {Describe(t.Snapshot, true, minTrackProbe)} saved={(t.Saved is { } saved ? Rect(saved) : "none")}");
        foreach (var s in known.Values.Where(s => !originalHandles.Contains(s.Handle)))
            Console.WriteLine($"{Stamp()} BASELINE        {Describe(s, true, minTrackProbe)}");

        var originalByHandle = originals.ToDictionary(o => o.Snapshot.Handle);
        // Stopwatch is monotonic; a wall-clock jump must not extend or shorten the bounded watch.
        var watchClock = Stopwatch.StartNew();
        var watchLimit = TimeSpan.FromSeconds(seconds);
        int newWindows = 0, closedWindows = 0, replacedWindows = 0, changedWindows = 0;
        while (watchClock.Elapsed < watchLimit)
        {
            await Task.Delay(PollMs).ConfigureAwait(false);
            double at = watchClock.Elapsed.TotalSeconds;
            var currentMatches = real.GetAppWindows().Where(plan.IsWatched).ToList();
            var current = currentMatches.GroupBy(w => w.Handle).ToDictionary(g => g.Key, g => g.First());

            // 1. The live original handles the restorer bound. Never move a binding to another window.
            foreach (var t in originals.Where(t => !t.Ended))
            {
                if (!current.TryGetValue(t.Snapshot.Handle, out var w))
                {
                    t.Ended = true;
                    closedWindows++;
                    Console.WriteLine($"{Stamp()} CLOSED          item={t.Id} original {Handle(t.Snapshot)} after {at:0.0}s; binding is NOT moved to another window");
                    continue;
                }
                var cur = Snap(w);
                known[t.Snapshot.Handle] = cur;
                if (!cur.SameIdentity(t.Snapshot))
                {
                    t.Ended = true;
                    replacedWindows++;
                    Console.WriteLine($"{Stamp()} REPLACED        item={t.Id} {Handle(t.Snapshot)} -> pid={cur.Pid} profile={ProfileLabel(cur.ProfileKey)} after {at:0.0}s; identity changed, NOT rebound");
                    continue;
                }
                if (!cur.SameFrame(t.Snapshot))
                {
                    changedWindows++;
                    bool hadImmediate = log.ImmediateMismatch.Contains(cur.Handle);
                    Console.WriteLine($"{Stamp()} LATE-CHANGE     item={t.Id} {Frame(t.Snapshot)} -> {Frame(cur)} saved={(t.Saved is { } saved ? Rect(saved) : "none")} after {at:0.0}s (same identity; observed after Place; cause not established" +
                                      (hadImmediate ? "; an immediate mismatch was also observed at Place" : "") + ")");
                    t.Snapshot = cur;
                    if (minTrackProbe.Query(cur.Handle) is { } mt)
                        Console.WriteLine($"{Stamp()}   MINTRACK-HINT handle=0x{cur.Handle:X} minTrackHint={mt.W}x{mt.H} (hint only; not proof)");
                }
            }

            // 2. New / closed / replaced windows with the same captured Chrome profile and exe identity.
            foreach (var w in currentMatches)
            {
                var cur = Snap(w);
                if (originalByHandle.ContainsKey(w.Handle)) { known[w.Handle] = cur; continue; }
                if (known.TryGetValue(w.Handle, out var old))
                {
                    if (!cur.SameIdentity(old))
                    {
                        known[w.Handle] = cur;
                        replacedWindows++;
                        Console.WriteLine($"{Stamp()} REPLACED-HANDLE handle=0x{w.Handle:X} pid={old.Pid} profile={ProfileLabel(old.ProfileKey)} -> pid={cur.Pid} profile={ProfileLabel(cur.ProfileKey)} after {at:0.0}s; new identity not adopted");
                    }
                    else if (!cur.SameFrame(old))
                    {
                        known[w.Handle] = cur;
                        changedWindows++;
                        Console.WriteLine($"{Stamp()} FRAME-CHANGE    handle=0x{w.Handle:X} pid={cur.Pid} profile={ProfileLabel(cur.ProfileKey)} {Frame(old)} -> {Frame(cur)} after {at:0.0}s (observed; cause not established)");
                    }
                }
                else
                {
                    known[w.Handle] = cur;
                    newWindows++;
                    Console.WriteLine($"{Stamp()} NEW             {Describe(cur, true, minTrackProbe)} after {at:0.0}s");
                    foreach (var t in originals.Where(t => t.Ended && !t.ReplacementReported && t.Snapshot.ProfileKey == cur.ProfileKey))
                    {
                        t.ReplacementReported = true;
                        replacedWindows++;
                        Console.WriteLine($"{Stamp()} REPLACEMENT     item={t.Id}: original 0x{t.Snapshot.Handle:X} is gone, new 0x{w.Handle:X} has the same captured profile; binding stays with the original");
                        break;
                    }
                }
            }

            foreach (long handle in known.Keys.Except(current.Keys).ToList())
            {
                var old = known[handle];
                known.Remove(handle);
                if (originalByHandle.ContainsKey(handle)) continue; // reported by the original loop
                closedWindows++;
                Console.WriteLine($"{Stamp()} CLOSED          handle=0x{handle:X} pid={old.Pid} profile={ProfileLabel(old.ProfileKey)}");
            }
        }

        Console.WriteLine($"{Stamp()} WATCH-END       after {watchClock.Elapsed.TotalSeconds:0.0}s of {seconds}s: new={newWindows} closed={closedWindows} replaced={replacedWindows} changed={changedWindows} placeAttempts={log.Places} minTrackQueries={minTrackProbe.Queried}");
        foreach (var t in originals)
            Console.WriteLine($"{Stamp()} FINAL           item={t.Id} original=0x{t.Snapshot.Handle:X} state={(t.Ended ? "ended" : "live")} last={Describe(t.Snapshot, false, minTrackProbe)}");
        return 0;
    }

    private sealed record WindowSnapshot(
        long Handle, int Pid, string ExeName, string ProfileKey, WindowShow Show, PixelRect Rect, string Monitor, int Dpi)
    {
        public bool SameIdentity(WindowSnapshot o) =>
            Handle == o.Handle && Pid == o.Pid && ProfileKey == o.ProfileKey
            && string.Equals(ExeName, o.ExeName, StringComparison.OrdinalIgnoreCase);

        public bool SameFrame(WindowSnapshot o) => Show == o.Show && Rect == o.Rect && Monitor == o.Monitor && Dpi == o.Dpi;
    }

    private sealed class OriginalWatch(string itemId, WindowSnapshot snapshot, PixelRect? saved)
    {
        public string Id { get; } = itemId;
        public WindowSnapshot Snapshot { get; set; } = snapshot;
        public PixelRect? Saved { get; } = saved;
        public bool Ended { get; set; }
        public bool ReplacementReported { get; set; }
    }

    private static WindowSnapshot Snap(WindowInfo w) => new(
        w.Handle, w.ProcessId, w.ExeName, BrowserSession.ProcessKey(w.ExePath, w.Arguments),
        w.Show, w.NormalRect, w.MonitorDevice, DpiOf(w.Handle));

    /// <summary>Wraps the real window system and logs each Place with the actual state immediately afterwards.</summary>
    private sealed class LoggingWindowSystem(
        IWindowSystem inner, Func<WindowInfo, bool> watched, Action<string> log, MinTrackProbe minTrack) : IWindowSystem
    {
        public int Places { get; private set; }
        public HashSet<long> ImmediateMismatch { get; } = [];

        public int OwnProcessId => inner.OwnProcessId;
        public IReadOnlyList<WindowInfo> GetAppWindows() => inner.GetAppWindows();
        public WindowInfo? Get(long handle) => inner.Get(handle);
        public bool IsAlive(long handle) => inner.IsAlive(handle);
        public IReadOnlyList<MonitorInfo> GetMonitors() => inner.GetMonitors();
        public (int L, int T, int R, int B) GetInvisibleBorders(long handle) => inner.GetInvisibleBorders(handle);
        public void BringToFront(long handle) => inner.BringToFront(handle);
        public bool Activate(long handle) => inner.Activate(handle);
        public void Close(long handle) => inner.Close(handle);
        public void Minimize(long handle) => inner.Minimize(handle);

        public PlaceOutcome Place(long handle, PixelRect normalRect, WindowShow show)
        {
            Places++;
            var before = inner.Get(handle);
            var outcome = inner.Place(handle, normalRect, show);
            var after = inner.Get(handle);
            log($"{Stamp()} PLACE           handle=0x{handle:X} pid={before?.ProcessId.ToString() ?? "?"} req={Rect(normalRect)} show={show} outcome={outcome}");
            if (after is null)
            {
                log($"{Stamp()}   ACTUAL        window gone immediately after Place");
                return outcome;
            }
            if (!watched(after))
            {
                log($"{Stamp()}   ACTUAL        handle no longer belongs to captured Chrome identity; details suppressed");
                return outcome;
            }
            log($"{Stamp()}   ACTUAL        {Describe(Snap(after), true, minTrack)}");
            bool rectOff = !NearRect(after.NormalRect, normalRect) || after.Show != show;
            if (rectOff)
            {
                ImmediateMismatch.Add(handle);
                log($"{Stamp()}   IMMEDIATE     requested {Rect(normalRect)} {show}, actual {Rect(after.NormalRect)} {after.Show}; " +
                    "requested bounds not kept (minimum/profile bounds possible; min-track is a hint)");
            }
            return outcome;
        }

        private static bool NearRect(PixelRect a, PixelRect b, int tol = 2) =>
            Math.Abs(a.X - b.X) <= tol && Math.Abs(a.Y - b.Y) <= tol &&
            Math.Abs(a.Width - b.Width) <= tol && Math.Abs(a.Height - b.Height) <= tol;
    }

    private static void PrintHeader(WatchPlan plan, int seconds)
    {
        Console.WriteLine("FlowDesk.Probe restore-watch - diagnostics only (no size enforcement, no rebinding)");
        Console.WriteLine($"date={DateTime.Now:yyyy-MM-dd HH:mm:ss} duration={seconds}s poll={PollMs}ms");
        Console.WriteLine("captured profile(s): " + string.Join(", ", plan.Profiles.Select(p => p.Label)));
        Console.WriteLine("scope: captured Chrome profile windows only; unrelated windows are never printed");
        Console.WriteLine("labels: scheme:leaf#hash (full command lines, URLs and titles are never printed)");
        Console.WriteLine(new string('-', 78));
    }

    private static string Handle(WindowSnapshot s) => $"handle=0x{s.Handle:X} pid={s.Pid} profile={ProfileLabel(s.ProfileKey)}";

    private static string Frame(WindowSnapshot s) => $"{s.Show} {Rect(s.Rect)} monitor={s.Monitor} dpi={s.Dpi}";

    private static string Describe(WindowSnapshot s, bool withMinTrack, MinTrackProbe minTrack)
    {
        string text = $"handle=0x{s.Handle:X} pid={s.Pid} exe={s.ExeName} profile={ProfileLabel(s.ProfileKey)} show={s.Show} normal={Rect(s.Rect)} monitor={s.Monitor} dpi={s.Dpi}";
        if (withMinTrack && minTrack.Query(s.Handle) is { } mt) text += $" minTrackHint={mt.W}x{mt.H} (hint)";
        return text;
    }

    private static string Rect(PixelRect r) => $"{r.X},{r.Y} {r.Width}x{r.Height}";

    private static string Stamp() => DateTime.Now.ToString("HH:mm:ss.fff");

    /// <summary>Caches min-track hints per handle and caps the number of windows queried (100 ms each).</summary>
    private sealed class MinTrackProbe
    {
        private const int MaxWindows = 60;
        private readonly Dictionary<long, Size2?> _cache = [];

        public int Queried => _cache.Count;

        public Size2? Query(long handle)
        {
            if (_cache.TryGetValue(handle, out var cached)) return cached;
            if (_cache.Count >= MaxWindows) return null;
            var result = QueryMinTrack(handle);
            _cache[handle] = result;
            return result;
        }
    }

    // WM_GETMINMAXINFO (0x24) is below WM_USER, so user32 system-marshals the MINMAXINFO pointer
    // across processes. It is still only a hint: timeout 100 ms, best-effort, never used for placement.
    private static Size2? QueryMinTrack(long handle)
    {
        try
        {
            var mmi = new MINMAXINFO();
            if (SendMessageTimeout((nint)handle, WM_GETMINMAXINFO, 0, ref mmi, SMTO_ABORTIFHUNG, 100, out _) == 0)
                return null;
            var p = mmi.ptMinTrackSize;
            return p.X > 0 && p.Y > 0 ? new Size2(p.X, p.Y) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static int DpiOf(long handle)
    {
        try
        {
            uint dpi = GetDpiForWindow((nint)handle);
            return dpi == 0 ? 0 : (int)dpi;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private readonly record struct Size2(int W, int H);

    private const uint WM_GETMINMAXINFO = 0x0024;
    private const uint SMTO_ABORTIFHUNG = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SendMessageTimeout(nint hWnd, uint msg, nint wParam, ref MINMAXINFO lParam,
        uint flags, uint timeout, out nint result);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hWnd);
}
