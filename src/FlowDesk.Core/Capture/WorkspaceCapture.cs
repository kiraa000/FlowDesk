using System.Diagnostics;
using FlowDesk.Core.Model;
using FlowDesk.Core.Restore;
using FlowDesk.Core.Windows;

namespace FlowDesk.Core.Capture;

/// <summary>An open window offered as a workspace item in the "save what's open" picker.</summary>
public sealed record CaptureCandidate(
    WindowInfo Window,
    WorkspaceItem Item,
    /// <summary>True when it is a browser: FlowDesk can't read its tabs, so the UI should ask for links.</summary>
    bool IsBrowser,
    /// <summary>Why it can't be included (null = it can).</summary>
    string? Problem);

/// <summary>Reads the live desktop into workspace items and saved window positions.</summary>
public sealed class WorkspaceCapture(IWindowSystem windows, SessionTracker tracker, IFolderPathSource? folders = null)
{
    private readonly IFolderPathSource _folders = folders ?? new ExplorerFolderPaths();

    /// <summary>Every open app window, front to back, turned into a proposed item (window position included).</summary>
    public IReadOnlyList<CaptureCandidate> CaptureOpenWindows()
    {
        var monitors = windows.GetMonitors();
        var all = windows.GetAppWindows().Where(w => w.ProcessId != windows.OwnProcessId).ToList();
        IReadOnlyDictionary<long, string> folderPaths = all.Any(w => w.ClassName == "CabinetWClass")
            ? _folders.GetOpenFolders() : new Dictionary<long, string>();

        var result = new List<CaptureCandidate>();
        for (int z = 0; z < all.Count; z++)
        {
            var w = all[z];
            var saved = ToSaved(w, z, monitors);
            if (w.Unresolved)
            {
                result.Add(new(w, new WorkspaceItem { Kind = ItemKind.App, Name = w.Title, Window = saved }, false,
                    "This app is suspended; open it once and refresh"));
                continue;
            }
            if (w.ClassName == "CabinetWClass")
            {
                folderPaths.TryGetValue(w.Handle, out var path);
                string folderName = path is not null && !path.StartsWith("::", StringComparison.Ordinal) && Path.GetFileName(path.TrimEnd('\\')) is { Length: > 0 } fn
                    ? fn : w.Title.Replace(" - File Explorer", "", StringComparison.Ordinal);
                result.Add(new(w, new WorkspaceItem { Kind = ItemKind.Folder, Name = folderName, Path = path, Window = saved }, false,
                    path is null ? "Couldn't read this folder's location" : null));
                continue;
            }
            if (string.IsNullOrEmpty(w.ExePath) && string.IsNullOrEmpty(w.Aumid))
            {
                result.Add(new(w, new WorkspaceItem { Kind = ItemKind.App, Name = w.Title, Window = saved }, false,
                    "Windows didn't let FlowDesk see which program this is"));
                continue;
            }
            var app = new AppRef
            {
                ExePath = w.ExePath, Aumid = w.Aumid,
                // Browsers: keep only the profile arguments. The rest of a browser's command line is whatever
                // started it (links, --new-window, -osint -url ...) and would reopen stale pages.
                Arguments = !string.IsNullOrEmpty(w.Aumid) ? null
                    : ShellLauncher.IsBrowser(w.ExePath) ? BrowserSession.ProfileArgs(w.ExePath, w.Arguments)
                    : w.Arguments,
            };
            result.Add(new(w, new WorkspaceItem { Kind = ItemKind.App, Name = DisplayName(w), App = app, Window = saved },
                ShellLauncher.IsBrowser(w.ExePath), null));
        }
        return result;
    }

    /// <summary>
    /// Records where each item's window is now ("Save layout"). Items whose window isn't open keep
    /// their old position. Returns the ids of items that were updated.
    /// </summary>
    public IReadOnlyList<string> SaveLayout(Workspace ws)
    {
        var monitors = windows.GetMonitors();
        var live = LiveWindowsFor(ws);
        var z = windows.GetAppWindows().Select((w, i) => (w.Handle, i)).ToDictionary(p => p.Handle, p => p.i);
        var updated = new List<string>();
        foreach (var item in ws.Items)
        {
            if (!live.TryGetValue(item.Id, out var w)) continue;
            item.Window = ToSaved(w, z.GetValueOrDefault(w.Handle, 999), monitors);
            updated.Add(item.Id);
        }
        if (updated.Count > 0) ws.LayoutSavedAt = DateTimeOffset.Now;
        return updated;
    }

    /// <summary>Current placements of a workspace's open windows, for the A -> B -> A session snapshot.</summary>
    public Dictionary<string, SavedWindow> SnapshotSession(Workspace ws)
    {
        var monitors = windows.GetMonitors();
        var z = windows.GetAppWindows().Select((w, i) => (w.Handle, i)).ToDictionary(p => p.Handle, p => p.i);
        return LiveWindowsFor(ws, bindNewMatches: false)
            .ToDictionary(kv => kv.Key, kv => ToSaved(kv.Value, z.GetValueOrDefault(kv.Value.Handle, 999), monitors));
    }

    /// <summary>Bound windows first; unbound items are matched against open windows (and then bound).</summary>
    public Dictionary<string, WindowInfo> LiveWindowsFor(Workspace ws, bool bindNewMatches = true)
    {
        var live = tracker.LiveWindows(windows, ws);
        var missing = ws.Items.Where(i => !live.ContainsKey(i.Id) && i.Kind != ItemKind.Website).ToList();
        if (missing.Count == 0) return live;
        var claimed = live.Values.Select(w => w.Handle).ToHashSet();
        var open = windows.GetAppWindows().Where(w => w.ProcessId != windows.OwnProcessId).ToList();
        foreach (var (id, w) in WindowMatcher.Assign(missing.Select(i => (i.Id, MatchSpec.For(i))).ToList(), open, claimed))
        {
            live[id] = w;
            if (bindNewMatches) tracker.Bind(ws.Id, id, w);
        }
        return live;
    }

    public static SavedWindow ToSaved(WindowInfo w, int zOrder, IReadOnlyList<MonitorInfo> monitors)
    {
        var m = monitors.FirstOrDefault(x => x.Device == w.MonitorDevice) ?? DisplayMapper.BestOverlap(w.NormalRect, monitors);
        return new SavedWindow
        {
            Rect = w.NormalRect, Show = w.Show, MonitorDevice = m?.Device ?? w.MonitorDevice,
            MonitorBounds = m?.Bounds ?? default, MonitorWorkArea = m?.WorkArea ?? default,
            Title = w.Title, ClassName = w.ClassName, ZOrder = zOrder,
        };
    }

    /// <summary>"Visual Studio Code" from the exe's version info; falls back to the exe name or the title.</summary>
    public static string DisplayName(WindowInfo w)
    {
        if (!string.IsNullOrEmpty(w.Aumid)) return LastTitlePart(w.Title);
        try
        {
            if (File.Exists(w.ExePath))
            {
                var vi = FileVersionInfo.GetVersionInfo(w.ExePath);
                if (!string.IsNullOrWhiteSpace(vi.FileDescription)) return vi.FileDescription.Trim();
                if (!string.IsNullOrWhiteSpace(vi.ProductName)) return vi.ProductName.Trim();
            }
        }
        catch (IOException) { }
        return string.IsNullOrEmpty(w.ExePath) ? w.Title : Path.GetFileNameWithoutExtension(w.ExePath);
    }

    /// <summary>"Inbox - Mail" -> "Mail": app names usually trail the title.</summary>
    private static string LastTitlePart(string title)
    {
        int i = title.LastIndexOfAny(['-', '—', '–']);
        return i >= 0 && i < title.Length - 2 ? title[(i + 1)..].Trim() : title;
    }
}
