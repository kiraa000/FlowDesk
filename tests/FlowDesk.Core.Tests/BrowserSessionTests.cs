using FlowDesk.Core.Model;
using FlowDesk.Core.Restore;
using Xunit;

namespace FlowDesk.Core.Tests;

public class BrowserSessionTests
{
    private const string Chrome = @"C:\Program Files\Google\Chrome\Application\chrome.exe";
    private const string Firefox = @"C:\Program Files\Mozilla Firefox\firefox.exe";

    [Fact]
    public void ProfileArgs_Chromium_KeepsProfileDropsLinksAndFlags()
    {
        string? args = BrowserSession.ProfileArgs(Chrome,
            "--new-window --user-data-dir=\"D:\\Temp\\fd profile\" --profile-directory=\"Profile 2\" https://example.com --flag-switches-begin");
        Assert.Equal("--user-data-dir=\"D:\\Temp\\fd profile\" --profile-directory=\"Profile 2\"", args);
    }

    [Fact]
    public void ProfileArgs_LinksOnly_IsNull()
    {
        Assert.Null(BrowserSession.ProfileArgs(Chrome, "--new-window \"https://a.example\" https://b.example"));
        Assert.Null(BrowserSession.ProfileArgs(Chrome, null));
    }

    [Fact]
    public void ProfileArgs_Gecko_KeepsProfileAndNoRemote()
    {
        Assert.Equal("-P \"Work stuff\" -no-remote",
            BrowserSession.ProfileArgs(Firefox, "-osint -url https://x.example -P \"Work stuff\" -no-remote"));
        Assert.Equal("-profile \"D:\\FF Profiles\\a\"", BrowserSession.ProfileArgs(Firefox, "--profile \"D:\\FF Profiles\\a\""));
    }

    [Fact]
    public void ProcessKey_ChromiumIgnoresProfileDirectory_LaunchKeyDoesNot()
    {
        string a = BrowserSession.ProcessKey(Chrome, "--user-data-dir=D:\\Temp\\p1\\ --profile-directory=Default");
        string b = BrowserSession.ProcessKey(Chrome, "--user-data-dir=\"d:/temp/P1\" --profile-directory=Work");
        Assert.Equal(a, b);
        Assert.Equal("", BrowserSession.ProcessKey(Chrome, "--profile-directory=Work"));

        var app1 = new AppRef { ExePath = Chrome, Arguments = "--profile-directory=Default" };
        var app2 = new AppRef { ExePath = Chrome, Arguments = "--profile-directory=Work" };
        var app3 = new AppRef { ExePath = Chrome };
        Assert.NotEqual(BrowserSession.LaunchKey(app1), BrowserSession.LaunchKey(app2));
        Assert.Equal(BrowserSession.LaunchKey(app1), BrowserSession.LaunchKey(app3)); // no flag = Default
    }

    [Fact]
    public void Applies_OnlyBrowserAppItemsWithOptionOn()
    {
        var item = Build.App(Chrome, "Inbox - Google Chrome");
        Assert.True(BrowserSession.Applies(item));
        item.ReopenLastTabs = false;
        Assert.False(BrowserSession.Applies(item));
        Assert.False(BrowserSession.Applies(Build.App(@"C:\Windows\notepad.exe", "x")));
    }

    [Fact]
    public void ReopenLastTabs_DefaultsTrue_WhenMissingFromOldJson()
    {
        var item = System.Text.Json.JsonSerializer.Deserialize<WorkspaceItem>("{\"Kind\":0,\"Name\":\"Chrome\"}")!;
        Assert.True(item.ReopenLastTabs);
        Assert.False(item.Clone() is { ReopenLastTabs: false });
    }

    [Fact]
    public void Matcher_NeverTakesAnotherProfilesWindow()
    {
        var item = Build.App(Chrome, "Docs - Google Chrome");
        item.App!.Arguments = "--user-data-dir=D:\\Temp\\p1";
        var spec = MatchSpec.For(item);
        var sys = new FakeWindowSystem();
        var other = sys.Add(Chrome, "Docs - Google Chrome", args: "--user-data-dir=D:\\Temp\\p2");
        var same = sys.Add(Chrome, "Something - Google Chrome", args: "--user-data-dir=\"d:\\temp\\p1\" --flag");
        var unreadable = sys.Add(Chrome, "Docs - Google Chrome", args: null);
        Assert.Equal(0, WindowMatcher.Score(other, spec));
        Assert.Equal(0, WindowMatcher.Score(unreadable, spec));
        Assert.True(WindowMatcher.Score(same, spec) >= WindowMatcher.Threshold);
    }

    [Fact]
    public async Task Restore_OneLaunchPerProfile_PlacesWindowsByTitle_LeavesExtras()
    {
        var sys = new FakeWindowSystem();
        var launcher = new FakeLauncher(sys);
        var ws = new Workspace { Name = "Web" };
        var mail = Build.App(Chrome, "Inbox - Google Chrome", rect: new(0, 0, 900, 1000));
        var docs = Build.App(Chrome, "Design doc - Google Docs - Google Chrome", rect: new(960, 0, 900, 1000));
        ws.Items.AddRange([mail, docs]);
        // One launch restores three windows; the extra one is left where the browser put it.
        launcher.Opens[mail.Id] =
        [
            (Chrome, "Design doc - Google Docs - Google Chrome", 2, null),
            (Chrome, "Inbox (3) - Google Chrome", 2, null),
            (Chrome, "Random page - Google Chrome", 2, null),
        ];
        var restorer = new WorkspaceRestorer(sys, launcher, new SessionTracker(), launcher.Delay);

        var report = await restorer.RestoreAsync(ws, Build.Fast());

        Assert.Equal([mail.Id], launcher.Launched);
        Assert.All(report.Items, r => Assert.Equal(ItemOutcome.Launched, r.Outcome));
        var byTitle = sys.Windows.ToDictionary(w => w.Title);
        Assert.Contains(sys.Placed, p => p.Handle == byTitle["Inbox (3) - Google Chrome"].Handle && p.Rect.X == 0);
        Assert.Contains(sys.Placed, p => p.Handle == byTitle["Design doc - Google Docs - Google Chrome"].Handle && p.Rect.X == 960);
        Assert.DoesNotContain(sys.Placed, p => p.Handle == byTitle["Random page - Google Chrome"].Handle);
    }

    [Fact]
    public async Task Restore_BrowserAlreadyRunning_ReusesOpenWindows_DoesNotLaunch()
    {
        var sys = new FakeWindowSystem();
        var launcher = new FakeLauncher(sys);
        var ws = new Workspace { Name = "Web" };
        var mail = Build.App(Chrome, "Inbox - Google Chrome");
        var docs = Build.App(Chrome, "Docs - Google Chrome");
        ws.Items.AddRange([mail, docs]);
        sys.Add(Chrome, "Inbox - Google Chrome");
        var restorer = new WorkspaceRestorer(sys, launcher, new SessionTracker(), launcher.Delay);

        var report = await restorer.RestoreAsync(ws, Build.Fast());

        Assert.Empty(launcher.Launched);
        Assert.Equal(ItemOutcome.Reused, report.Items.Single(r => r.ItemId == mail.Id).Outcome);
        Assert.Equal(ItemOutcome.Skipped, report.Items.Single(r => r.ItemId == docs.Id).Outcome);
    }

    [Fact]
    public async Task Restore_OtherProfileRunning_StillLaunchesThisProfile()
    {
        var sys = new FakeWindowSystem();
        var launcher = new FakeLauncher(sys);
        var ws = new Workspace { Name = "Test" };
        var item = Build.App(Chrome, "Test page - Google Chrome");
        item.App!.Arguments = "--user-data-dir=D:\\Temp\\fdtest";
        ws.Items.Add(item);
        var personal = sys.Add(Chrome, "Test page - Google Chrome"); // default profile: must not be moved
        launcher.Opens[item.Id] = [(Chrome, "Test page - Google Chrome", 1, "--user-data-dir=D:\\Temp\\fdtest")];
        var restorer = new WorkspaceRestorer(sys, launcher, new SessionTracker(), launcher.Delay);

        var report = await restorer.RestoreAsync(ws, Build.Fast());

        Assert.Equal([item.Id], launcher.Launched);
        Assert.Equal(ItemOutcome.Launched, report.Items.Single().Outcome);
        Assert.DoesNotContain(sys.Placed, p => p.Handle == personal.Handle);
    }

    [Fact]
    public async Task Restore_SessionLaunchFails_SiblingsFailImmediately()
    {
        var sys = new FakeWindowSystem();
        var launcher = new FakeLauncher(sys);
        var ws = new Workspace { Name = "Web" };
        var a = Build.App(Chrome, "A - Google Chrome");
        var b = Build.App(Chrome, "B - Google Chrome");
        ws.Items.AddRange([a, b]);
        launcher.Failing.Add(a.Id);
        var restorer = new WorkspaceRestorer(sys, launcher, new SessionTracker(), launcher.Delay);

        var report = await restorer.RestoreAsync(ws, Build.Fast());

        Assert.Equal([a.Id], launcher.Launched);
        Assert.All(report.Items, r => Assert.Equal(ItemOutcome.LaunchFailed, r.Outcome));
    }

    [Fact]
    public async Task Restore_OptionOff_KeepsOldPerItemLaunch()
    {
        var sys = new FakeWindowSystem();
        var launcher = new FakeLauncher(sys);
        var ws = new Workspace { Name = "Web" };
        var a = Build.App(Chrome, "A - Google Chrome");
        var b = Build.App(Chrome, "B - Google Chrome");
        a.ReopenLastTabs = b.ReopenLastTabs = false;
        ws.Items.AddRange([a, b]);
        var restorer = new WorkspaceRestorer(sys, launcher, new SessionTracker(), launcher.Delay);

        await restorer.RestoreAsync(ws, Build.Fast(maxPolls: 2));

        Assert.Equal([a.Id, b.Id], launcher.Launched);
    }
}
