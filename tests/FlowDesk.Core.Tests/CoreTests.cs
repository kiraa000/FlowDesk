using FlowDesk.Core.Focus;
using FlowDesk.Core.Layout;
using FlowDesk.Core.Model;
using FlowDesk.Core.Restore;
using FlowDesk.Core.Storage;
using FlowDesk.Core.Windows;
using Microsoft.Extensions.Time.Testing;

namespace FlowDesk.Core.Tests;

public class DisplayMapperTests
{
    private static readonly MonitorInfo Primary = new("\\\\.\\DISPLAY1", new(0, 0, 1920, 1080), new(0, 0, 1920, 1040), true, 96);

    [Fact]
    public void Same_monitor_keeps_exact_rect()
    {
        var s = Build.App("a", "t", new(100, 100, 800, 600)).Window!;
        Assert.Equal(new PixelRect(100, 100, 800, 600), DisplayMapper.Map(s, [Primary]));
    }

    [Fact]
    public void Unplugged_monitor_maps_proportionally_onto_primary()
    {
        var s = new SavedWindow
        {
            Rect = new(-2560 + 1280, 0, 1280, 1367), MonitorDevice = "\\\\.\\DISPLAY2",
            MonitorBounds = new(-2560, 0, 2560, 1440), MonitorWorkArea = new(-2560, 0, 2560, 1367),
        };
        var r = DisplayMapper.Map(s, [Primary]);
        Assert.Equal(960, r.X);   // right half of the old screen -> right half of the primary
        Assert.Equal(960, r.Width);
        Assert.True(r.Bottom <= 1040 + 16);
    }

    [Fact]
    public void Offscreen_window_is_pulled_back_into_reach()
    {
        var r = DisplayMapper.KeepVisible(new(5000, -300, 800, 600), Primary.WorkArea);
        Assert.InRange(r.X, 0, 1920 - 800 + 16);
        Assert.Equal(0, r.Y);
    }

    [Fact]
    public void Visible_frame_conversion_adds_invisible_borders()
    {
        Assert.Equal(new PixelRect(93, 100, 214, 207), DisplayMapper.ToOuter(new(100, 100, 200, 200), (7, 0, 7, 7)));
    }
}

public class MatcherAndMiscTests
{
    private static WindowInfo W(string exe, string title, string cls = "Win", string? aumid = null) => new()
    {
        Handle = title.GetHashCode(), ProcessId = 5, ExePath = exe, Aumid = aumid, Title = title, ClassName = cls,
        NormalRect = new(0, 0, 10, 10), Show = WindowShow.Normal,
    };

    [Fact]
    public void Exe_name_matches_after_app_update_moved_the_exe()
    {
        var spec = MatchSpec.For(Build.App(@"C:\Discord\app-1.0.1\Discord.exe", "Discord"));
        Assert.True(WindowMatcher.Score(W(@"C:\Discord\app-1.0.2\Discord.exe", "Discord"), spec) >= WindowMatcher.Threshold);
    }

    [Fact]
    public void Packaged_app_matches_by_aumid_only()
    {
        var spec = MatchSpec.For(Build.App("", "Notepad", aumid: "Microsoft.WindowsNotepad_8wekyb3d8bbwe!App"));
        Assert.Equal(0, WindowMatcher.Score(W(@"C:\x\notepad.exe", "Untitled - Notepad"), spec));
        Assert.True(WindowMatcher.Score(W(@"C:\x\notepad.exe", "Untitled - Notepad", aumid: "microsoft.windowsnotepad_8wekyb3d8bbwe!app"), spec) >= 100);
    }

    [Theory]
    [InlineData("\"C:\\a b\\x.exe\" --foo bar", "--foo bar")]
    [InlineData("x.exe", null)]
    [InlineData("C:\\x.exe  -n  ", "-n")]
    public void StripExe_removes_program_part(string cmd, string? expected) => Assert.Equal(expected, ProcessResolver.StripExe(cmd));

    [Fact]
    public void FindUpdatedExe_picks_newest_versioned_folder()
    {
        string root = Path.Combine(Path.GetTempPath(), "fd-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            foreach (var v in new[] { "app-1.0.9", "app-1.0.10" })
            {
                Directory.CreateDirectory(Path.Combine(root, v));
                File.WriteAllText(Path.Combine(root, v, "Discord.exe"), "");
            }
            var found = ShellLauncher.FindUpdatedExe(Path.Combine(root, "app-1.0.8", "Discord.exe"));
            Assert.Equal(Path.Combine(root, "app-1.0.10", "Discord.exe"), found);
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(LayoutPreset.TwoColumns, 3)]
    [InlineData(LayoutPreset.PrimarySidebar, 1)]
    [InlineData(LayoutPreset.Grid, 7)]
    [InlineData(LayoutPreset.Cascade, 9)]
    public void Layouts_give_one_rect_per_window_inside_the_work_area(LayoutPreset preset, int n)
    {
        var rects = LayoutEngine.Compute(preset, n);
        Assert.Equal(n, rects.Count);
        Assert.All(rects, r => Assert.True(r.X >= 0 && r.Y >= 0 && r.X + r.W <= 1.0001 && r.Y + r.H <= 1.0001 && r.W > 0.1));
    }
}

public class FocusWatcherTests
{
    private readonly FakeWindowSystem _sys = new();
    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-11-10T10:00:00Z"));

    private FocusWatcher Start(FocusStrictness mode, out List<AppBlockedEvent> events)
    {
        var watcher = new FocusWatcher(_sys, _time);
        var list = new List<AppBlockedEvent>();
        watcher.AppBlocked += (_, e) => list.Add(e);
        watcher.Start(new FocusSettings
        {
            DurationMinutes = 25, Strictness = mode,
            Blocked = [new FocusAppRule { Name = "Discord", ExeName = "discord" }],
            Allowed = [new FocusAppRule { Name = "Code", ExeName = "code" }],
        }, TimeSpan.FromHours(1)); // timer effectively off; tests call Tick()
        events = list;
        return watcher;
    }

    [Fact]
    public void Gentle_warns_once_per_window_and_never_closes()
    {
        var w = Start(FocusStrictness.Gentle, out var events);
        _sys.Add(@"C:\d\Discord.exe", "Discord");
        w.Tick(); w.Tick();
        Assert.Single(events);
        Assert.Equal(BlockAction.Warned, events[0].Action);
        Assert.Empty(_sys.Closed);
    }

    [Fact]
    public void Normal_closes_and_allow_5_min_lets_it_back_until_expiry()
    {
        var w = Start(FocusStrictness.Normal, out var events);
        var d = _sys.Add(@"C:\d\Discord.exe", "Discord");
        w.Tick();
        Assert.Equal([d.Handle], _sys.Closed);
        Assert.True(events[0].CanAllowTemporarily);

        Assert.True(w.AllowTemporarily(d));
        _sys.Windows.Clear();
        var d2 = _sys.Add(@"C:\d\Discord.exe", "Discord");
        w.Tick();
        Assert.DoesNotContain(d2.Handle, _sys.Closed);

        _time.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));
        w.Tick();
        Assert.Contains(d2.Handle, _sys.Closed);
    }

    [Fact]
    public void Window_ignoring_close_is_minimized_and_closed_again()
    {
        var w = Start(FocusStrictness.Strict, out _);
        var d = _sys.Add(@"C:\d\Discord.exe", "Discord");
        w.Tick();
        _time.Advance(TimeSpan.FromSeconds(3));
        w.Tick();
        Assert.Equal(2, _sys.Closed.Count(h => h == d.Handle));
        Assert.Contains(d.Handle, _sys.Minimized);
    }

    [Fact]
    public void Strict_cannot_be_stopped_or_allowed_without_force()
    {
        var w = Start(FocusStrictness.Strict, out _);
        var d = _sys.Add(@"C:\d\Discord.exe", "Discord");
        Assert.False(w.AllowTemporarily(d));
        Assert.False(w.Stop());
        Assert.NotNull(w.State);
        Assert.True(w.Stop(force: true));
        Assert.Null(w.State);
    }

    [Fact]
    public void Allowed_system_and_own_windows_are_never_touched_even_in_block_all_mode()
    {
        var watcher = new FocusWatcher(_sys, _time);
        var s = new FocusSettings { BlockAllExceptAllowed = true, Allowed = [new FocusAppRule { ExeName = "code" }] };
        Assert.False(watcher.IsBlocked(_sys.Add(@"C:\x\Code.exe", "Code"), s, out _));
        Assert.False(watcher.IsBlocked(_sys.Add(@"C:\Windows\explorer.exe", "Downloads"), s, out _));
        Assert.False(watcher.IsBlocked(_sys.Add(@"C:\x\FlowDesk.exe", "FlowDesk", pid: 1), s, out _));
        Assert.False(watcher.IsBlocked(_sys.Add("", "Admin thing"), s, out _));
        Assert.True(watcher.IsBlocked(_sys.Add(@"C:\x\steam.exe", "Steam"), s, out _));
    }

    [Fact]
    public void Session_ends_by_itself_and_pause_extends_it()
    {
        var w = Start(FocusStrictness.Normal, out _);
        bool ended = false;
        w.SessionEnded += (_, _) => ended = true;
        _time.Advance(TimeSpan.FromMinutes(10));
        w.Pause();
        _time.Advance(TimeSpan.FromMinutes(30));
        Assert.Equal(TimeSpan.FromMinutes(15), w.Remaining);
        w.Resume();
        _time.Advance(TimeSpan.FromMinutes(15));
        w.Tick();
        Assert.True(ended);
        Assert.Null(w.State);
    }
}

public class JsonStoreTests
{
    [Fact]
    public async Task Round_trips_and_recovers_from_a_corrupt_file()
    {
        string dir = Path.Combine(Path.GetTempPath(), "fd-store-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new JsonStore(dir);
            var data = new FlowDeskData { Workspaces = [new Workspace { Name = "Study", Items = [Build.App(@"C:\a.exe", "A")] }] };
            await store.SaveAsync(data);
            data.Workspaces[0].Name = "Study 2";
            await store.SaveAsync(data); // creates .bak with "Study"

            var loaded = store.Load(out var rec);
            Assert.Null(rec);
            Assert.Equal("Study 2", loaded.Workspaces[0].Name);
            Assert.Equal(new PixelRect(100, 100, 900, 700), loaded.Workspaces[0].Items[0].Window!.Rect);

            File.WriteAllText(store.FilePath, "{ not json");
            var recovered = store.Load(out rec);
            Assert.NotNull(rec);
            Assert.True(File.Exists(rec));
            Assert.Equal("Study", recovered.Workspaces[0].Name);
        }
        finally { Directory.Delete(dir, true); }
    }
}
