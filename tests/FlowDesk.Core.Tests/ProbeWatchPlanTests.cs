using FlowDesk.Core.Model;
using FlowDesk.Core.Windows;
using FlowDesk.Probe;

namespace FlowDesk.Core.Tests;

/// <summary>
/// Probe restore-watch must never restore/launch a broad workspace: every item has to be a Chrome
/// App/Website item with a captured, non-empty --user-data-dir and no AUMID. The plan compares the
/// full exe identity plus ProcessKey, not just the "chrome" basename.
/// </summary>
public class ProbeWatchPlanTests
{
    private const string Chrome = @"C:\Program Files\Google\Chrome\Application\chrome.exe";
    private const string Profile = @"--user-data-dir=D:\Temp\chrome-profile";

    [Fact]
    public void Accepts_only_chrome_app_and_website_items_with_captured_profile()
    {
        var app = Build.App(Chrome, "Inbox - Google Chrome");
        app.App!.Arguments = Profile;
        var site = new WorkspaceItem
        {
            Kind = ItemKind.Website, Name = "Docs",
            App = new AppRef { ExePath = Chrome, Arguments = Profile },
            Urls = ["https://example.com"],
        };

        Assert.True(RestoreWatchPlan.TryCreate(new Workspace { Items = [app, site] }, out var plan, out var error), error);
        Assert.Single(plan.Profiles);
        Assert.DoesNotContain("D:\\Temp", plan.Profiles[0].Label); // leaf + hash only
        Assert.True(plan.IsCapturedChromeItem(app));
        Assert.True(plan.IsCapturedChromeItem(site));

        var inner = new FakeWindowSystem();
        Assert.True(plan.IsWatched(inner.Add(Chrome, "Inbox - Google Chrome", args: Profile)));
        // Same basename but a different exe identity: never in scope.
        Assert.False(plan.IsWatched(inner.Add(@"C:\Other\Chrome\Application\chrome.exe", "Inbox - Google Chrome", args: Profile)));
        Assert.False(plan.IsWatched(inner.Add(Chrome, "Docs - Google Chrome", args: @"--user-data-dir=D:\Temp\other")));
        Assert.False(plan.IsWatched(inner.Add(Chrome, "Inbox - Google Chrome", args: Profile, aumid: "Some.Aumid")));
    }

    [Fact]
    public void Rejects_nonbrowser_items_in_a_broad_workspace()
    {
        var chrome = Build.App(Chrome, "Chrome");
        chrome.App!.Arguments = Profile;
        var notepad = Build.App(@"C:\Windows\notepad.exe", "notes.txt - Notepad");

        Assert.False(RestoreWatchPlan.TryCreate(new Workspace { Items = [chrome, notepad] }, out _, out var error));
        Assert.Contains("only watches Chrome", error);
    }

    [Fact]
    public void Rejects_file_items_even_when_they_name_chrome()
    {
        var file = new WorkspaceItem
        {
            Kind = ItemKind.File, Name = "report.txt", Path = @"D:\report.txt",
            App = new AppRef { ExePath = Chrome, Arguments = Profile },
        };

        Assert.False(RestoreWatchPlan.TryCreate(new Workspace { Items = [file] }, out _, out var error));
        Assert.Contains("requires Chrome App/Website items only", error);
    }

    [Fact]
    public void Rejects_default_chrome_profile()
    {
        var item = Build.App(Chrome, "Chrome");

        Assert.False(RestoreWatchPlan.TryCreate(new Workspace { Items = [item] }, out _, out var error));
        Assert.Contains("refusing the default profile", error);
    }

    [Fact]
    public void Rejects_website_without_captured_chrome_app()
    {
        var site = new WorkspaceItem { Kind = ItemKind.Website, Name = "Docs", Urls = ["https://example.com"] };

        Assert.False(RestoreWatchPlan.TryCreate(new Workspace { Items = [site] }, out _, out var error));
        Assert.Contains("no captured Chrome executable", error);
    }

    [Fact]
    public void Rejects_website_with_default_chrome_profile()
    {
        var site = new WorkspaceItem
        {
            Kind = ItemKind.Website, Name = "Docs",
            App = new AppRef { ExePath = Chrome },
            Urls = ["https://example.com"],
        };

        Assert.False(RestoreWatchPlan.TryCreate(new Workspace { Items = [site] }, out _, out var error));
        Assert.Contains("refusing the default profile", error);
    }

    [Fact]
    public void Rejects_aumid_chrome_items()
    {
        var item = Build.App(Chrome, "Chrome", aumid: "Some.Aumid");
        item.App!.Arguments = Profile;

        Assert.False(RestoreWatchPlan.TryCreate(new Workspace { Items = [item] }, out _, out var error));
        Assert.Contains("AUMID", error);
    }

    [Fact]
    public void Rejects_non_chrome_executable()
    {
        var other = Build.App(@"C:\apps\notchrome.exe", "Other app");
        other.App!.Arguments = Profile;

        Assert.False(RestoreWatchPlan.TryCreate(new Workspace { Items = [other] }, out _, out var error));
        Assert.Contains("only watches Chrome", error);
    }
}
