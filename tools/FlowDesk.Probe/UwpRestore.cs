// uwp-restore: prove what FlowDesk.Place does to ONE exact user-started packaged app (Calculator) whose
// top-level window is an ApplicationFrameWindow owned by ApplicationFrameHost.exe while the app itself is a
// child CoreWindow owned by CalculatorApp.exe. The command never launches or closes an app: it minimizes and
// restores only the single frame whose child pid is exactly <CalculatorAppPid>.
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using FlowDesk.Core.Layout;
using FlowDesk.Core.Model;
using FlowDesk.Core.Windows;

namespace FlowDesk.Probe;

internal static class UwpRestore
{
    public const int MinSeconds = 1;
    public const int MaxSeconds = 10;
    public const int DefaultSeconds = 3;
    private const string CalculatorProcessName = "CalculatorApp";
    private const string FrameHostProcessName = "ApplicationFrameHost";
    private const int PollMs = 100;
    private const int IconicWaitMs = 50;
    private const int IconicWaitTries = 20;

    public static void PrintUsage()
    {
        Console.WriteLine("uwp-restore usage:");
        Console.WriteLine("  uwp-restore <CalculatorAppPid> [seconds]    (whole seconds, 1..10, default 3)");
        Console.WriteLine("  The pid must be the CalculatorApp.exe process INSIDE the Calculator ApplicationFrameWindow,");
        Console.WriteLine("  not ApplicationFrameHost.exe. Opus, from an elevated-free PowerShell:");
        Console.WriteLine("    Get-Process CalculatorApp | Select-Object Id, ProcessName, StartTime");
        Console.WriteLine("    .\\FlowDesk.Probe.exe uwp-restore <Id> 3");
        Console.WriteLine("  Refuses a pid that is not CalculatorApp, or whose exact child pid is not under exactly one frame.");
        Console.WriteLine("  Never selects by window title. Two phases (Restore, Layout), each minimizes first and polls raw state.");
    }

    public static async Task<int> RunAsync(int appPid, int seconds)
    {
        if (appPid <= 0)
        {
            Console.Error.WriteLine("uwp-restore: the CalculatorApp pid must be a positive integer");
            PrintUsage();
            return 2;
        }
        if (seconds < MinSeconds || seconds > MaxSeconds)
        {
            Console.Error.WriteLine($"uwp-restore: duration must be a whole number {MinSeconds}..{MaxSeconds} seconds");
            PrintUsage();
            return 2;
        }

        Win32WindowSystem.EnsurePerMonitorDpiAwareness();
        var real = new Win32WindowSystem();

        if (!TryVerifyCalculator(appPid, out var startedAt, out string exeName, out string? processError))
        {
            Console.Error.WriteLine("uwp-restore: " + processError);
            return 2;
        }

        var candidates = EnumerateFrameChildren();
        if (!UwpRestorePlan.TryChoose(appPid, candidates, out var choice, out string? planError))
        {
            Console.Error.WriteLine("uwp-restore: " + planError);
            return 2;
        }
        if (choice.FramePid == appPid || !IsFrameHostProcess(choice.FramePid))
        {
            Console.Error.WriteLine($"uwp-restore: frame 0x{choice.Frame:X} pid={choice.FramePid} is not {FrameHostProcessName} with a different child pid; refusing");
            return 2;
        }

        Console.WriteLine("FlowDesk.Probe uwp-restore - exact packaged-app frame, two bounded phases");
        Console.WriteLine($"date={DateTime.Now:yyyy-MM-dd HH:mm:ss} pid={appPid} exe={exeName} start={startedAt:yyyy-MM-dd HH:mm:ss} seconds={seconds}");
        Console.WriteLine($"frame=0x{choice.Frame:X} framePid={choice.FramePid} child=0x{choice.Child:X} childPid={choice.ChildPid} childClass={choice.ChildClass}");
        Console.WriteLine("legend: core=Win32WindowSystem.Get, native=IsIconic/IsHungAppWindow; no title is used or printed");
        Console.WriteLine(new string('-', 78));

        var original = real.Get(choice.Frame) ?? throw new InvalidOperationException("frame disappeared before capture");
        var monitors = real.GetMonitors();
        var monitor = monitors.FirstOrDefault(m => m.Device == original.MonitorDevice)
                      ?? monitors.FirstOrDefault(m => Contains(m.Bounds, original.NormalRect))
                      ?? monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors.FirstOrDefault();
        var desiredNormal = monitor is null
            ? original.NormalRect
            : DisplayMapper.KeepVisible(original.NormalRect, monitor.WorkArea);
        Console.WriteLine($"ORIGINAL        coreShow={original.Show} coreNormal={Rect(original.NormalRect)} frameMonitor={(monitor?.Device ?? "?")} desiredNormal={Rect(desiredNormal)}");
        Console.WriteLine($"BEFORE          {Snapshot(real, choice.Frame, appPid)}");

        bool phase1Restored = false, phase2Restored = false;
        PlaceOutcome? phase1Outcome = null, phase2Outcome = null;
        try
        {
            Console.WriteLine("-- PHASE 1: Restore (minimize the exact frame, then the same Win32WindowSystem.Place path)");
            if (!SameIdentity(real, choice, appPid, startedAt))
            {
                Console.WriteLine("PHASE 1 REFUSED exact frame/child/ApplicationFrameHost/Process.StartTime identity no longer valid");
                return 5;
            }
            if (!await MinimizeAndWaitAsync(real, choice.Frame)) return 3;
            Console.WriteLine($"MINIMIZED       {Snapshot(real, choice.Frame, appPid)}");
            phase1Outcome = real.Place(choice.Frame, desiredNormal, WindowShow.Normal);
            Console.WriteLine($"PLACE           handle=0x{choice.Frame:X} target={Rect(desiredNormal)} show=Normal outcome={phase1Outcome}");
            await PollAsync(real, choice.Frame, appPid, "RESTORE", seconds);
            phase1Restored = phase1Outcome == PlaceOutcome.Ok && !IsIconic((nint)choice.Frame)
                && (monitor is null || real.Get(choice.Frame)?.MonitorDevice == monitor.Device);
            Console.WriteLine($"PHASE 1 RESULT  outcome={phase1Outcome} iconic={IsIconic((nint)choice.Frame)} " +
                              $"monitor={real.Get(choice.Frame)?.MonitorDevice ?? "?"} targetMonitor={monitor?.Device ?? "?"} restored={phase1Restored}");

            Console.WriteLine("-- PHASE 2: Layout (LayoutApplier on this single frame's monitor)");
            if (!SameIdentity(real, choice, appPid, startedAt))
            {
                Console.WriteLine("PHASE 2 REFUSED exact frame/child/ApplicationFrameHost/Process.StartTime identity no longer valid");
                return 5;
            }
            if (!await MinimizeAndWaitAsync(real, choice.Frame)) return 3;
            Console.WriteLine($"MINIMIZED       {Snapshot(real, choice.Frame, appPid)}");
            var layoutWindow = real.Get(choice.Frame);
            if (layoutWindow is null)
            {
                Console.WriteLine("LAYOUT          frame disappeared before layout");
                phase2Outcome = PlaceOutcome.WindowGone;
                return 4;
            }
            var results = new LayoutApplier(real).Apply(LayoutPreset.TwoColumns, [layoutWindow], layoutWindow.MonitorDevice);
            foreach (var (handle, result) in results)
                Console.WriteLine($"LAYOUT          handle=0x{handle:X} outcome={result} monitor={layoutWindow.MonitorDevice}");
            await PollAsync(real, choice.Frame, appPid, "LAYOUT", seconds);
            phase2Outcome = results.Count == 1 ? results[0].Outcome : PlaceOutcome.Failed;
            phase2Restored = phase2Outcome == PlaceOutcome.Ok && !IsIconic((nint)choice.Frame)
                && real.Get(choice.Frame)?.MonitorDevice == layoutWindow.MonitorDevice;
            Console.WriteLine($"PHASE 2 RESULT  outcome={phase2Outcome} iconic={IsIconic((nint)choice.Frame)} " +
                              $"monitor={real.Get(choice.Frame)?.MonitorDevice ?? "?"} targetMonitor={layoutWindow.MonitorDevice} restored={phase2Restored}");
            return phase1Restored && phase2Restored ? 0 : 4;
        }
        finally
        {
            bool identity = SameIdentity(real, choice, appPid, startedAt);
            Console.WriteLine($"FINALLY         identityValid={identity} scope=exact-frame-only");
            if (identity)
            {
                var result = real.Place(choice.Frame, original.NormalRect, original.Show);
                Console.WriteLine($"RESTORED        handle=0x{choice.Frame:X} target={Rect(original.NormalRect)} show={original.Show} outcome={result}");
                Console.WriteLine($"AFTER           {Snapshot(real, choice.Frame, appPid)}");
            }
            else
            {
                Console.WriteLine("RESTORE REFUSED original bounds/show not re-applied: frame/child/process identity changed");
            }
            Console.WriteLine($"SUMMARY         phase1Outcome={phase1Outcome} phase1Restored={phase1Restored} " +
                              $"phase2Outcome={phase2Outcome} phase2Restored={phase2Restored} " +
                              $"finalIconic={IsIconic((nint)choice.Frame)} finalHung={IsHungAppWindow((nint)choice.Frame)}");
        }
    }

    private static bool TryVerifyCalculator(int pid, out DateTime startedAt, out string exeName, out string? error)
    {
        startedAt = default;
        exeName = "";
        error = null;
        try
        {
            using var process = Process.GetProcessById(pid);
            exeName = ExeNameOf(process);
            if (!string.Equals(Path.GetFileNameWithoutExtension(exeName), CalculatorProcessName, StringComparison.OrdinalIgnoreCase))
            {
                error = $"pid {pid} is {exeName}, not {CalculatorProcessName}.exe";
                return false;
            }
            startedAt = process.StartTime;
            return true;
        }
        catch (Exception ex)
        {
            error = $"cannot verify pid {pid}: {ex.Message}";
            return false;
        }
    }

    private static string ExeNameOf(Process process)
    {
        try { return process.MainModule?.FileName is { Length: > 0 } path ? Path.GetFileName(path) : process.ProcessName + ".exe"; }
        catch (Exception) { return process.ProcessName + ".exe"; }
    }

    private static bool IsFrameHostProcess(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return string.Equals(process.ProcessName, FrameHostProcessName, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool SameIdentity(Win32WindowSystem real, UwpFrameChoice choice, int appPid, DateTime startedAt)
    {
        try
        {
            if (!IsWindow((nint)choice.Frame)) return false;
            GetWindowThreadProcessId((nint)choice.Frame, out uint framePid);
            if (framePid != (uint)choice.FramePid || framePid == (uint)appPid) return false;
            if (!IsFrameHostProcess(choice.FramePid)) return false;
            var current = real.Get(choice.Frame);
            if (current is null || current.ProcessId != appPid) return false;
            var children = EnumerateFrameChildren().Where(c => c.Frame == choice.Frame && c.ChildPid == appPid).ToList();
            if (children.Count == 0) return false;
            using var process = Process.GetProcessById(appPid);
            return string.Equals(Path.GetFileNameWithoutExtension(ExeNameOf(process)), CalculatorProcessName, StringComparison.OrdinalIgnoreCase)
                && process.StartTime == startedAt;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static async Task<bool> MinimizeAndWaitAsync(Win32WindowSystem real, long frame)
    {
        real.Minimize(frame);
        for (int i = 0; i < IconicWaitTries && !IsIconic((nint)frame); i++) await Task.Delay(IconicWaitMs);
        if (IsIconic((nint)frame)) return true;
        Console.WriteLine($"MINIMIZE FAILED handle=0x{frame:X}: not iconic after {IconicWaitTries * IconicWaitMs} ms");
        return false;
    }

    private static async Task PollAsync(Win32WindowSystem real, long frame, int appPid, string phase, int seconds)
    {
        var clock = Stopwatch.StartNew();
        int polls = 0;
        while (clock.Elapsed < TimeSpan.FromSeconds(seconds))
        {
            await Task.Delay(PollMs);
            polls++;
            Console.WriteLine($"POLL {phase,-7} t={clock.Elapsed.TotalMilliseconds,5:0}ms {Snapshot(real, frame, appPid)}");
        }
        Console.WriteLine($"POLL-END {phase,-5} polls={polls} elapsed={clock.Elapsed.TotalMilliseconds:0}ms");
    }

    private static string Snapshot(Win32WindowSystem real, long frame, int appPid)
    {
        var info = real.Get(frame);
        var child = EnumerateFrameChildren().FirstOrDefault(c => c.Frame == frame && c.ChildPid == appPid);
        string childText = child == default
            ? "child=- pid=- class=-"
            : $"child=0x{child.Child:X} childPid={child.ChildPid} childClass={child.ChildClass}";
        return $"frame=0x{frame:X} framePid={FramePid(frame)} resolvedAppPid={info?.ProcessId.ToString() ?? "-"} {childText} " +
               $"coreShow={info?.Show.ToString() ?? "-"} coreNormal={(info is null ? "-" : Rect(info.NormalRect))} " +
               $"nativeIconic={IsIconic((nint)frame)} nativeHung={IsHungAppWindow((nint)frame)}";
    }

    private static int FramePid(long frame)
    {
        GetWindowThreadProcessId((nint)frame, out uint pid);
        return (int)pid;
    }

    private static List<FrameChild> EnumerateFrameChildren()
    {
        var list = new List<FrameChild>();
        EnumWindows((frame, _) =>
        {
            if (!string.Equals(ClassName(frame), "ApplicationFrameWindow", StringComparison.Ordinal)) return true;
            GetWindowThreadProcessId(frame, out uint rawFramePid);
            int framePid = (int)rawFramePid;
            EnumChildWindows(frame, (child, _) =>
            {
                GetWindowThreadProcessId(child, out uint rawChildPid);
                int childPid = (int)rawChildPid;
                if (childPid != 0 && childPid != framePid)
                    list.Add(new FrameChild((long)frame, framePid, (long)child, childPid, ClassName(child)));
                return true;
            }, 0);
            return true;
        }, 0);
        return list;
    }

    private static string ClassName(nint h)
    {
        var sb = new StringBuilder(256);
        return GetClassNameW(h, sb, sb.Capacity) > 0 ? sb.ToString() : "";
    }

    private static string Rect(PixelRect r) => $"{r.X},{r.Y} {r.Width}x{r.Height}";

    private static bool Contains(PixelRect outer, PixelRect point) =>
        point.X >= outer.X && point.X < outer.Right && point.Y >= outer.Y && point.Y < outer.Bottom;

    private delegate bool EnumWindowsProc(nint hwnd, nint lParam);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, nint lParam);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(nint parent, EnumWindowsProc callback, nint lParam);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsHungAppWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassNameW(nint hwnd, StringBuilder sb, int max);
}
