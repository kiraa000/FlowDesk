// FlowDesk.Probe - exercise the window core on the real desktop.
//   list                         print open app windows (what capture sees)
//   capture <file.json>          save all open, includable windows as a workspace
//   capture-match <file.json> <substring>
//                                like capture, but only windows whose title, exe path or arguments
//                                contain <substring> (case-insensitive): isolate one disposable app/profile
//                                e.g. Chrome started with
//                                --user-data-dir="D:\Workspace\_projects\FlowDesk\sol-tasks\realtest-001\chrome-profile"
//                                then: capture-match ...\realtest-001\chrome.json chrome-profile
//   restore <file.json>          open/move everything back, print the report
//   restore-watch <file.json> <1..60 seconds>
//                                restore once, then poll captured Chrome-profile windows every 250 ms:
//                                Place attempts with actual rect identity/DPI, late resize, closure, replacement
//   layout <grid|two|sidebar|cascade> <file.json>   tile the workspace's open windows
//   uwp-restore <CalculatorAppPid> [seconds 1..10, default 3]
//                                minimize the ONE ApplicationFrameWindow whose child pid is exactly that
//                                CalculatorApp.exe process, then run Place (Restore) and LayoutApplier
//                                (Layout), printing raw frame/child pids, core show/rect, native iconic/hung.
//                                Refuses zero/multiple frames and non-CalculatorApp pids; no title guessing.
//                                Opus steps: start Calculator by hand, then in PowerShell:
//                                  Get-Process CalculatorApp | Select-Object Id, ProcessName, StartTime
//                                  .\FlowDesk.Probe.exe uwp-restore <Id> 3
//   desktops [dllDir]            read-only virtual desktop check via VirtualDesktopAccessor.dll
//   focus <exeName> <seconds> [gentle|normal|strict] block an app for a while
using System.Text.Json;
using FlowDesk.Core.Capture;
using FlowDesk.Core.Focus;
using FlowDesk.Core.Layout;
using FlowDesk.Core.Model;
using FlowDesk.Core.Restore;
using FlowDesk.Core.Storage;
using FlowDesk.Core.Windows;
using FlowDesk.Probe;

var ws = new Win32WindowSystem();
var tracker = new SessionTracker();
var capture = new WorkspaceCapture(ws, tracker);
string cmd = args.Length > 0 ? args[0] : "list";

switch (cmd)
{
    case "desktops": // read-only: desktops <dir-with-dll>  count, current, foreground window on current?
    {
        using var vd = FlowDesk.Core.Desktops.VirtualDesktopAccessor.Load(
            [Path.Combine(args.Length > 1 ? args[1] : AppContext.BaseDirectory, FlowDesk.Core.Desktops.VirtualDesktopAccessor.DllName)]);
        Console.WriteLine($"available={vd.IsAvailable} status=\"{vd.Status}\"");
        Console.WriteLine($"count={vd.GetCount()} current={vd.GetCurrent()}");
        long fg = ProbeForeground();
        Console.WriteLine($"foreground={fg} onCurrent={vd.IsWindowOnCurrent(fg)}");
        break;
    }
    case "list":
        foreach (var c in capture.CaptureOpenWindows())
        {
            var w = c.Window;
            Console.WriteLine($"{w.Handle,10} pid={w.ProcessId,-6} {w.Show,-9} {w.NormalRect} {w.MonitorDevice}");
            Console.WriteLine($"           {c.Item.Kind} \"{c.Item.Name}\" title=\"{Trim(w.Title)}\" class={w.ClassName}");
            Console.WriteLine($"           exe={w.ExePath} aumid={w.Aumid} args={Trim(w.Arguments)} path={c.Item.Path}" +
                              (c.IsBrowser ? " [browser]" : "") + (c.Problem is null ? "" : $" PROBLEM: {c.Problem}"));
        }
        foreach (var m in ws.GetMonitors()) Console.WriteLine($"monitor {m.Device} bounds={m.Bounds} work={m.WorkArea} dpi={m.Dpi} primary={m.IsPrimary}");
        break;

    case "capture":
    {
        var items = capture.CaptureOpenWindows().Where(c => c.Problem is null).Select(c => c.Item).ToList();
        var workspace = new Workspace { Name = "Probe capture", Items = items, LayoutSavedAt = DateTimeOffset.Now };
        File.WriteAllText(args[1], JsonSerializer.Serialize(workspace, JsonStore.Options));
        Console.WriteLine($"saved {items.Count} items to {args[1]}");
        break;
    }

    case "capture-match":
    {
        if (args.Length < 3) { Console.WriteLine("usage: capture-match <file.json> <substring>"); break; }
        string needle = args[2];
        bool Hit(string? s) => s is not null && s.Contains(needle, StringComparison.OrdinalIgnoreCase);
        var items = capture.CaptureOpenWindows()
            .Where(c => c.Problem is null && (Hit(c.Window.Title) || Hit(c.Window.ExePath) || Hit(c.Window.Arguments)))
            .Select(c => c.Item).ToList();
        var workspace = new Workspace { Name = "Probe capture", Items = items, LayoutSavedAt = DateTimeOffset.Now };
        File.WriteAllText(args[1], JsonSerializer.Serialize(workspace, JsonStore.Options));
        Console.WriteLine($"saved {items.Count} items matching \"{needle}\" to {args[1]}");
        break;
    }

    case "restore":
    {
        var workspace = Load(args[1]);
        var restorer = new WorkspaceRestorer(ws, new ShellLauncher(), tracker);
        var progress = new Progress<ItemResult>(r => { if (r.Outcome != ItemOutcome.Pending) Console.WriteLine($"  .. {r.Name}: {r.Outcome}"); });
        var report = await restorer.RestoreAsync(workspace, progress: progress);
        Console.WriteLine($"restored {report.Placed}/{report.Items.Count} in {report.Elapsed.TotalSeconds:0.0}s");
        foreach (var r in report.Items) Console.WriteLine($"  {r.Outcome,-16} {r.Name} {r.Detail}");
        break;
    }

    case "restore-watch":
    {
        if (args.Length < 3) { RestoreWatch.PrintUsage(); Environment.ExitCode = 2; break; }
        Environment.ExitCode = await RestoreWatch.RunAsync(Load(args[1]), args[2]);
        break;
    }

    case "layout":
    {
        var preset = args[1] switch { "two" => LayoutPreset.TwoColumns, "sidebar" => LayoutPreset.PrimarySidebar, "cascade" => LayoutPreset.Cascade, _ => LayoutPreset.Grid };
        var workspace = Load(args[2]);
        var live = capture.LiveWindowsFor(workspace);
        var ordered = workspace.Items.Where(i => live.ContainsKey(i.Id)).Select(i => live[i.Id]).ToList();
        foreach (var (h, o) in new LayoutApplier(ws).Apply(preset, ordered)) Console.WriteLine($"  {h}: {o}");
        break;
    }

    case "uwp-restore":
    {
        if (args.Length < 2 || !int.TryParse(args[1], out int appPid))
        {
            UwpRestore.PrintUsage();
            Environment.ExitCode = 2;
            break;
        }
        int seconds = UwpRestore.DefaultSeconds;
        if (args.Length > 2 && (!int.TryParse(args[2], out seconds)
            || seconds < UwpRestore.MinSeconds || seconds > UwpRestore.MaxSeconds))
        {
            Console.Error.WriteLine($"uwp-restore: seconds must be a whole number {UwpRestore.MinSeconds}..{UwpRestore.MaxSeconds}, got \"{args[2]}\"");
            UwpRestore.PrintUsage();
            Environment.ExitCode = 2;
            break;
        }
        Environment.ExitCode = await UwpRestore.RunAsync(appPid, seconds);
        break;
    }

    case "focus":
    {
        var settings = new FocusSettings
        {
            DurationMinutes = 1,
            Strictness = args.Length > 3 ? Enum.Parse<FocusStrictness>(args[3], true) : FocusStrictness.Normal,
            Blocked = [new FocusAppRule { Name = args[1], ExeName = args[1] }],
        };
        using var watcher = new FocusWatcher(ws);
        watcher.AppBlocked += (_, e) => Console.WriteLine($"  {DateTime.Now:T} {e.Action} {e.AppName} \"{Trim(e.Window.Title)}\" allow5={e.CanAllowTemporarily}");
        watcher.Start(settings);
        await Task.Delay(TimeSpan.FromSeconds(args.Length > 2 ? int.Parse(args[2]) : 30));
        watcher.Stop(force: true);
        Console.WriteLine("focus probe done");
        break;
    }

    case "switch":
    {
        // switch <titleSubstrA[,..]> <titleSubstrB[,..]>: A -> B -> A -> A on the matching open windows, printing states.
        var all = capture.CaptureOpenWindows().Where(c => c.Problem is null).ToList();
        Workspace Pick(string name, string filters)
        {
            var f = filters.Split(',', StringSplitOptions.RemoveEmptyEntries);
            var items = all.Where(c => f.Any(x => (c.Window.Title ?? "").Contains(x, StringComparison.OrdinalIgnoreCase)))
                .Select(c => c.Item).ToList();
            return new Workspace { Name = name, Items = items, LayoutSavedAt = DateTimeOffset.Now };
        }
        var a = Pick("A", args[1]); var b = Pick("B", args[2]);
        Console.WriteLine($"A={a.Items.Count} items, B={b.Items.Count} items");
        if (args.Length > 3) { Console.WriteLine($"waiting {args[3]}s (close some A windows now)"); await Task.Delay(int.Parse(args[3]) * 1000); }
        var switcher = new WorkspaceSwitcher(ws, tracker, capture, new WorkspaceRestorer(ws, new ShellLauncher(), tracker));
        void Dump(string step)
        {
            Console.WriteLine($"-- {step}");
            foreach (var (id, w) in capture.LiveWindowsFor(a, bindNewMatches: false))
            {
                var item = a.Items.First(i => i.Id == id);
                var sess = tracker.GetSession(a.Id);
                string s = sess is not null && sess.TryGetValue(id, out var sw) ? sw.Show.ToString() : "-";
                Console.WriteLine($"   A.{item.Name}: live={ws.Get(w.Handle)?.Show} saved={item.Window?.Show} session={s}");
            }
        }
        Dump("start");
        var r1 = await switcher.SwitchToAsync(a, null); await Task.Delay(1500); Dump($"open A ({r1.Placed}/{r1.Items.Count})");
        var r2 = await switcher.SwitchToAsync(b, a); await Task.Delay(1500); Dump($"A->B ({r2.Placed}/{r2.Items.Count})");
        var r3 = await switcher.SwitchToAsync(a, b); await Task.Delay(1500); Dump($"B->A ({r3.Placed}/{r3.Items.Count})");
        await Task.Delay(8000); Dump("B->A +9.5s");
        var r4 = await switcher.SwitchToAsync(a, a); await Task.Delay(1500); Dump($"A->A ({r4.Placed}/{r4.Items.Count})");
        break;
    }

    default:
        Console.WriteLine("usage: list | capture <f> | capture-match <f> <substring> | restore <f> | restore-watch <f> <1..60> | layout <preset> <f> | uwp-restore <CalculatorAppPid> [1..10] | focus <exe> <sec> [mode] | switch <a> <b>");
        break;
}

static Workspace Load(string path) =>
    JsonSerializer.Deserialize<Workspace>(File.ReadAllText(path), JsonStore.Options) ?? throw new InvalidDataException(path);

static string Trim(string? s) => s is null ? "" : s.Length > 60 ? s[..57] + "..." : s;

[System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetForegroundWindow")]
static extern nint ProbeForeground();
