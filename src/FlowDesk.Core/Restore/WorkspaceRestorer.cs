using FlowDesk.Core.Model;
using FlowDesk.Core.Windows;

namespace FlowDesk.Core.Restore;

public enum ItemOutcome
{
    /// <summary>Not finished yet (only seen in progress reports).</summary>
    Pending,
    /// <summary>An already-open window was used and moved into place.</summary>
    Reused,
    /// <summary>Started the item and placed its new window.</summary>
    Launched,
    /// <summary>Started the item, but no window could be identified in time (it may still open, or went into an existing window).</summary>
    LaunchedNoWindow,
    /// <summary>Could not start the item (missing file, program not found, ...).</summary>
    LaunchFailed,
    /// <summary>Found the window but Windows refused to move it (it runs as administrator).</summary>
    PlaceDenied,
    /// <summary>Deliberately not started; the detail says why (e.g. the browser is already running).</summary>
    Skipped,
}

public sealed record ItemResult(string ItemId, string Name, ItemOutcome Outcome, string? Detail = null);

public sealed record RestoreReport(IReadOnlyList<ItemResult> Items, TimeSpan Elapsed)
{
    public int Placed => Items.Count(i => i.Outcome is ItemOutcome.Reused or ItemOutcome.Launched);
    public bool AllPlaced => Placed == Items.Count;
}

public sealed class RestoreOptions
{
    /// <summary>Use windows that are already open instead of launching a second copy.</summary>
    public bool ReuseOpenWindows { get; init; } = true;
    /// <summary>How long to wait for launched items to show a window.</summary>
    public TimeSpan LaunchTimeout { get; init; } = TimeSpan.FromSeconds(20);
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(250);
    /// <summary>Apps often move themselves right after opening; re-place windows this long after placing them.</summary>
    public TimeSpan SettleDelay { get; init; } = TimeSpan.FromMilliseconds(1200);
    /// <summary>Bring the workspace's top window to the foreground at the end.</summary>
    public bool Activate { get; init; } = true;
    /// <summary>Placements to use instead of each item's saved window (session snapshot); missing ids fall back to the saved one.</summary>
    public IReadOnlyDictionary<string, SavedWindow>? Overrides { get; init; }
}

/// <summary>
/// Opens a workspace: reuses or launches every item, waits for its window, and puts each
/// window back where it was saved. Safe to call from a background thread; never blocks on a hung app.
/// </summary>
public sealed class WorkspaceRestorer(IWindowSystem windows, IAppLauncher launcher, SessionTracker tracker,
    Func<TimeSpan, CancellationToken, Task>? delay = null, Func<string?>? defaultBrowser = null)
{
    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? Task.Delay;
    private readonly Func<string?> _defaultBrowser = defaultBrowser ?? ShellLauncher.DefaultBrowserExe;

    public async Task<RestoreReport> RestoreAsync(Workspace ws, RestoreOptions? options = null,
        IProgress<ItemResult>? progress = null, CancellationToken ct = default)
    {
        var opt = options ?? new RestoreOptions();
        var started = DateTime.UtcNow;
        var results = new Dictionary<string, ItemResult>();
        var matched = new Dictionary<string, WindowInfo>();
        var claimed = new HashSet<long>();
        var specs = new Dictionary<string, MatchSpec>();
        string? browser = null;

        void Report(WorkspaceItem item, ItemOutcome o, string? detail = null)
        {
            var r = new ItemResult(item.Id, item.Name, o, detail);
            results[item.Id] = r;
            progress?.Report(r);
        }

        SavedWindow? Placement(WorkspaceItem item) =>
            opt.Overrides is not null && opt.Overrides.TryGetValue(item.Id, out var s) ? s : item.Window;

        foreach (var item in ws.Items)
        {
            if (item.Kind == ItemKind.Website) browser ??= item.App?.ExePath is { Length: > 0 } p ? p : _defaultBrowser();
            specs[item.Id] = MatchSpec.For(item, item.Kind == ItemKind.Website ? (item.App?.ExePath is { Length: > 0 } p2 ? p2 : browser) : null);
        }

        // 1. Windows this workspace already owns in this session.
        foreach (var item in ws.Items)
            if (tracker.Live(windows, ws.Id, item.Id) is { } live && claimed.Add(live.Handle))
                matched[item.Id] = live;

        // 2. Other open windows that fit (not for websites: an existing browser window holds other tabs).
        var open = windows.GetAppWindows().Where(w => w.ProcessId != windows.OwnProcessId).ToList();
        if (opt.ReuseOpenWindows)
        {
            var candidates = ws.Items.Where(i => !matched.ContainsKey(i.Id) && i.Kind != ItemKind.Website)
                                     .Select(i => (i.Id, specs[i.Id])).ToList();
            foreach (var (id, w) in WindowMatcher.Assign(candidates, open, claimed))
            {
                matched[id] = w;
                claimed.Add(w.Handle);
            }
        }
        foreach (var item in ws.Items.Where(i => matched.ContainsKey(i.Id)))
            Report(item, ItemOutcome.Pending, "Using the open window");

        // 3. Launch the rest, then wait for their windows.
        var before = open.Select(w => w.Handle).ToHashSet();
        var pending = new List<WorkspaceItem>();
        // Browser session restore: ONE launch per browser profile brings back all its windows; the other items of
        // that profile just wait for those windows (matched by title). Extra restored windows stay unclaimed.
        var launchedSessions = new Dictionary<string, string?>(); // launch key -> launch error (null = started)
        foreach (var item in ws.Items.Where(i => !matched.ContainsKey(i.Id)))
        {
            ct.ThrowIfCancellationRequested();
            string? sessionKey = null;
            if (BrowserSession.Applies(item))
            {
                // Already running: a launch would only add an empty window, and the open windows already hold the
                // tabs. Decided from the windows open BEFORE this restore launched anything.
                if (open.Any(w => WindowMatcher.Score(w, specs[item.Id] with { Title = null }) > 0))
                {
                    Report(item, ItemOutcome.Skipped, "The browser is already open, so its open windows keep their tabs");
                    continue;
                }
                sessionKey = BrowserSession.LaunchKey(item.App!);
                if (launchedSessions.TryGetValue(sessionKey, out var sessionError))
                {
                    if (sessionError is not null) { Report(item, ItemOutcome.LaunchFailed, sessionError); continue; }
                    pending.Add(item);
                    Report(item, ItemOutcome.Pending, "Waiting for the browser to reopen its windows");
                    continue;
                }
            }
            var lr = launcher.Launch(item);
            if (sessionKey is not null) launchedSessions[sessionKey] = lr.Ok ? null : lr.Error ?? "Could not start the browser";
            if (!lr.Ok) { Report(item, ItemOutcome.LaunchFailed, lr.Error); continue; }
            if (item.Kind == ItemKind.File && lr.HostExe is { } host && specs[item.Id].ExeName is null or "")
                specs[item.Id] = specs[item.Id] with { ExeName = AppKeys.ExeName(host) };
            pending.Add(item);
            Report(item, ItemOutcome.Pending, "Opening");
        }

        var launchedIds = new HashSet<string>();
        // Counted in polls rather than wall-clock time: deterministic, and a slow machine still gets every poll.
        int maxPolls = Math.Max(1, (int)(opt.LaunchTimeout / opt.PollInterval));
        for (int poll = 1; pending.Count > 0 && poll <= maxPolls; poll++)
        {
            await _delay(opt.PollInterval, ct).ConfigureAwait(false);
            var now = windows.GetAppWindows().Where(w => w.ProcessId != windows.OwnProcessId).ToList();
            // First half of the wait: only brand-new windows count. Later, an existing unclaimed window may do
            // (single-instance apps that only bring their old window forward).
            bool lateFallback = poll > maxPolls / 2;
            var assigned = WindowMatcher.Assign(
                pending.Select(i => (i.Id, specs[i.Id])).ToList(), now, claimed,
                bonus: (_, w) => before.Contains(w.Handle) ? 0 : 40,
                allowed: (_, w) => lateFallback || !before.Contains(w.Handle));
            foreach (var (id, w) in assigned)
            {
                matched[id] = w;
                claimed.Add(w.Handle);
                launchedIds.Add(id);
            }
            pending.RemoveAll(i => matched.ContainsKey(i.Id));
            // Place as they arrive, so the user sees progress.
            foreach (var (id, w) in assigned)
                PlaceOne(ws.Items.First(i => i.Id == id), w, Placement, Report, launched: true);
        }
        foreach (var item in pending)
            Report(item, ItemOutcome.LaunchedNoWindow, "Opened, but its window was not found in time");

        // 4. Place reused windows.
        foreach (var item in ws.Items.Where(i => matched.ContainsKey(i.Id) && !launchedIds.Contains(i.Id)))
            PlaceOne(item, matched[item.Id], Placement, Report, launched: false);

        // 5. Settle: apps that restore their own size after showing get put back once more.
        if (launchedIds.Count > 0 && opt.SettleDelay > TimeSpan.Zero)
        {
            await _delay(opt.SettleDelay, ct).ConfigureAwait(false);
            var monitors = windows.GetMonitors();
            foreach (var id in launchedIds)
            {
                var item = ws.Items.First(i => i.Id == id);
                if (Placement(item) is not { } saved || windows.Get(matched[id].Handle) is not { } cur) continue;
                var target = DisplayMapper.Map(saved, monitors);
                if (cur.Show != saved.Show || (saved.Show == WindowShow.Normal && !Near(cur.NormalRect, target)))
                    windows.Place(cur.Handle, target, saved.Show);
            }
        }

        // 6. Stack in saved order (bottom first) and focus the top one.
        var stack = ws.Items.Where(i => matched.ContainsKey(i.Id) && results[i.Id].Outcome is ItemOutcome.Reused or ItemOutcome.Launched)
                            .OrderByDescending(i => Placement(i)?.ZOrder ?? int.MaxValue).ToList();
        foreach (var item in stack)
            if (Placement(item)?.Show != WindowShow.Minimized) windows.BringToFront(matched[item.Id].Handle);
        if (opt.Activate && stack.LastOrDefault(i => Placement(i)?.Show != WindowShow.Minimized) is { } top)
            windows.Activate(matched[top.Id].Handle);

        foreach (var (id, w) in matched) tracker.Bind(ws.Id, id, w);

        var ordered = ws.Items.Select(i => results.TryGetValue(i.Id, out var r) ? r : new ItemResult(i.Id, i.Name, ItemOutcome.LaunchedNoWindow)).ToList();
        return new RestoreReport(ordered, DateTime.UtcNow - started);
    }

    private void PlaceOne(WorkspaceItem item, WindowInfo w, Func<WorkspaceItem, SavedWindow?> placement,
        Action<WorkspaceItem, ItemOutcome, string?> report, bool launched)
    {
        var ok = launched ? ItemOutcome.Launched : ItemOutcome.Reused;
        if (placement(item) is not { } saved) { report(item, ok, "No saved position"); return; }
        var target = DisplayMapper.Map(saved, windows.GetMonitors());
        switch (windows.Place(w.Handle, target, saved.Show))
        {
            case PlaceOutcome.Ok: report(item, ok, null); break;
            case PlaceOutcome.AccessDenied: report(item, ItemOutcome.PlaceDenied, "Runs as administrator, so FlowDesk can't move it"); break;
            case PlaceOutcome.WindowGone: report(item, ItemOutcome.LaunchedNoWindow, "The window closed"); break;
            default: report(item, ok, "Could not move the window"); break;
        }
    }

    private static bool Near(PixelRect a, PixelRect b, int tol = 8) =>
        Math.Abs(a.X - b.X) <= tol && Math.Abs(a.Y - b.Y) <= tol &&
        Math.Abs(a.Width - b.Width) <= tol && Math.Abs(a.Height - b.Height) <= tol;
}
