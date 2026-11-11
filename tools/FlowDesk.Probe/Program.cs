// FlowDesk.Probe - exercise the window core on the real desktop.
//   list                         print open app windows (what capture sees)
//   capture <file.json>          save all open, includable windows as a workspace
//   restore <file.json>          open/move everything back, print the report
//   layout <grid|two|sidebar|cascade> <file.json>   tile the workspace's open windows
//   focus <exeName> <seconds> [gentle|normal|strict] block an app for a while
using System.Text.Json;
using FlowDesk.Core.Capture;
using FlowDesk.Core.Focus;
using FlowDesk.Core.Layout;
using FlowDesk.Core.Model;
using FlowDesk.Core.Restore;
using FlowDesk.Core.Storage;
using FlowDesk.Core.Windows;

var ws = new Win32WindowSystem();
var tracker = new SessionTracker();
var capture = new WorkspaceCapture(ws, tracker);
string cmd = args.Length > 0 ? args[0] : "list";

switch (cmd)
{
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

    case "layout":
    {
        var preset = args[1] switch { "two" => LayoutPreset.TwoColumns, "sidebar" => LayoutPreset.PrimarySidebar, "cascade" => LayoutPreset.Cascade, _ => LayoutPreset.Grid };
        var workspace = Load(args[2]);
        var live = capture.LiveWindowsFor(workspace);
        var ordered = workspace.Items.Where(i => live.ContainsKey(i.Id)).Select(i => live[i.Id]).ToList();
        foreach (var (h, o) in new LayoutApplier(ws).Apply(preset, ordered)) Console.WriteLine($"  {h}: {o}");
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

    default:
        Console.WriteLine("usage: list | capture <f> | restore <f> | layout <preset> <f> | focus <exe> <sec> [mode]");
        break;
}

static Workspace Load(string path) =>
    JsonSerializer.Deserialize<Workspace>(File.ReadAllText(path), JsonStore.Options) ?? throw new InvalidDataException(path);

static string Trim(string? s) => s is null ? "" : s.Length > 60 ? s[..57] + "..." : s;
