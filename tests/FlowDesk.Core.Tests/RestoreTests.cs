using FlowDesk.Core.Capture;
using FlowDesk.Core.Model;
using FlowDesk.Core.Restore;
using FlowDesk.Core.Windows;

namespace FlowDesk.Core.Tests;

public class RestoreTests
{
    private readonly FakeWindowSystem _sys = new();
    private readonly SessionTracker _tracker = new();
    private readonly FakeLauncher _launcher;
    private readonly WorkspaceRestorer _restorer;

    public RestoreTests()
    {
        _launcher = new FakeLauncher(_sys);
        _restorer = new WorkspaceRestorer(_sys, _launcher, _tracker, _launcher.Delay, () => @"C:\b\chrome.exe");
    }

    [Fact]
    public async Task Reuses_open_window_and_moves_it_without_launching()
    {
        var w = _sys.Add(@"C:\apps\code.exe", "proj - Visual Studio Code");
        var item = Build.App(@"C:\apps\code.exe", "proj - Visual Studio Code", new(200, 50, 1000, 800));
        var report = await _restorer.RestoreAsync(new Workspace { Items = [item] }, Build.Fast());

        Assert.Empty(_launcher.Launched);
        Assert.Equal(ItemOutcome.Reused, report.Items[0].Outcome);
        Assert.Contains(_sys.Placed, p => p.Handle == w.Handle && p.Rect == new PixelRect(200, 50, 1000, 800));
        Assert.Equal(w.Handle, _sys.Activated);
    }

    [Fact]
    public async Task Launches_missing_app_waits_for_its_window_then_places_it()
    {
        var item = Build.App(@"C:\apps\notes.exe", "Notes", new(0, 0, 640, 480));
        _launcher.Behaviour[item.Id] = (@"C:\apps\notes.exe", "Notes", 3);
        var report = await _restorer.RestoreAsync(new Workspace { Items = [item] }, Build.Fast());

        Assert.Equal(ItemOutcome.Launched, report.Items[0].Outcome);
        Assert.Single(_sys.Placed);
        Assert.Equal(new PixelRect(0, 0, 640, 480), _sys.Placed[0].Rect);
    }

    [Fact]
    public async Task Reports_failures_and_timeouts_per_item()
    {
        var bad = Build.App(@"C:\gone.exe", "Gone");
        var slow = Build.App(@"C:\apps\slow.exe", "Slow");
        _launcher.Failing.Add(bad.Id);
        _launcher.Behaviour[slow.Id] = (@"C:\apps\slow.exe", "Slow", 999);
        var report = await _restorer.RestoreAsync(new Workspace { Items = [bad, slow] }, Build.Fast(maxPolls: 5));

        Assert.Equal(ItemOutcome.LaunchFailed, report.Items[0].Outcome);
        Assert.Equal("Program not found", report.Items[0].Detail);
        Assert.Equal(ItemOutcome.LaunchedNoWindow, report.Items[1].Outcome);
        Assert.False(report.AllPlaced);
    }

    [Fact]
    public async Task Two_windows_of_one_app_go_to_the_right_items_by_title()
    {
        var a = _sys.Add(@"C:\apps\code.exe", "alpha - Visual Studio Code");
        var b = _sys.Add(@"C:\apps\code.exe", "beta - Visual Studio Code");
        var ia = Build.App(@"C:\apps\code.exe", "alpha - Visual Studio Code", new(0, 0, 500, 500));
        var ib = Build.App(@"C:\apps\code.exe", "beta - Visual Studio Code", new(600, 0, 500, 500));
        await _restorer.RestoreAsync(new Workspace { Items = [ib, ia] }, Build.Fast());

        Assert.Contains(_sys.Placed, p => p.Handle == a.Handle && p.Rect.X == 0);
        Assert.Contains(_sys.Placed, p => p.Handle == b.Handle && p.Rect.X == 600);
    }

    [Fact]
    public async Task Website_never_hijacks_an_existing_browser_window()
    {
        var existing = _sys.Add(@"C:\b\chrome.exe", "Mail - Chrome");
        var site = new WorkspaceItem { Kind = ItemKind.Website, Name = "Docs", Urls = ["https://example.com"], Window = Build.App("x", "Docs").Window };
        _launcher.Behaviour[site.Id] = (@"C:\b\chrome.exe", "Example Domain - Chrome", 2);
        var report = await _restorer.RestoreAsync(new Workspace { Items = [site] }, Build.Fast());

        Assert.Single(_launcher.Launched);
        Assert.Equal(ItemOutcome.Launched, report.Items[0].Outcome);
        Assert.DoesNotContain(_sys.Placed, p => p.Handle == existing.Handle);
    }

    [Fact]
    public async Task Admin_window_is_reported_not_silently_ignored()
    {
        var w = _sys.Add(@"C:\apps\regedit.exe", "Registry Editor");
        _sys.Denied.Add(w.Handle);
        var report = await _restorer.RestoreAsync(new Workspace { Items = [Build.App(@"C:\apps\regedit.exe", "Registry Editor")] }, Build.Fast());
        Assert.Equal(ItemOutcome.PlaceDenied, report.Items[0].Outcome);
    }

    [Fact]
    public async Task Folder_needs_explorer_window_with_its_name()
    {
        _sys.Add(@"C:\Windows\explorer.exe", "Downloads", "CabinetWClass");
        var target = _sys.Add(@"C:\Windows\explorer.exe", "Projects", "CabinetWClass");
        var folder = new WorkspaceItem { Kind = ItemKind.Folder, Name = "Projects", Path = @"D:\Projects", Window = Build.App("x", "Projects").Window };
        await _restorer.RestoreAsync(new Workspace { Items = [folder] }, Build.Fast());
        Assert.Contains(_sys.Placed, p => p.Handle == target.Handle);
        Assert.Single(_sys.Placed);
    }

    [Fact]
    public async Task Switch_A_B_A_restores_A_as_it_was_left_and_tucks_away_A_only_windows()
    {
        var capture = new WorkspaceCapture(_sys, _tracker, new NoFolders());
        var switcher = new WorkspaceSwitcher(_sys, _tracker, capture, _restorer);
        var code = _sys.Add(@"C:\apps\code.exe", "proj - Visual Studio Code");
        var spotify = _sys.Add(@"C:\apps\spotify.exe", "Spotify");
        var a = new Workspace { Name = "A", Items = [Build.App(@"C:\apps\code.exe", "proj - Visual Studio Code", new(0, 0, 900, 900)), Build.App(@"C:\apps\spotify.exe", "Spotify", new(1000, 0, 600, 600))] };
        var b = new Workspace { Name = "B", Items = [Build.App(@"C:\apps\code.exe", "proj - Visual Studio Code", new(500, 500, 400, 400))] };

        await switcher.SwitchToAsync(a, null, ct: default);
        // The user moves Code while in A.
        _sys.Place(code.Handle, new PixelRect(42, 42, 999, 777), WindowShow.Normal);

        await switcher.SwitchToAsync(b, a);
        Assert.Contains(spotify.Handle, _sys.Minimized);      // A-only window tucked away
        Assert.DoesNotContain(code.Handle, _sys.Minimized);   // shared window just moved
        Assert.Equal(new PixelRect(500, 500, 400, 400), _sys.Get(code.Handle)!.NormalRect);

        await switcher.SwitchToAsync(a, b);
        Assert.Equal(new PixelRect(42, 42, 999, 777), _sys.Get(code.Handle)!.NormalRect); // as left, not as saved
        Assert.Equal(WindowShow.Normal, _sys.Get(spotify.Handle)!.Show);
        Assert.Equal(new PixelRect(1000, 0, 600, 600), _sys.Get(spotify.Handle)!.NormalRect);
    }

    private sealed class NoFolders : IFolderPathSource
    {
        public IReadOnlyDictionary<long, string> GetOpenFolders() => new Dictionary<long, string>();
    }
}
