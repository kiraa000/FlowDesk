using FlowDesk.App.Services;
using FlowDesk.Core.Model;

namespace FlowDesk.App.Tests;

public sealed class BrowserNoticeSelectionTests
{
    [Fact]
    public void OnlyToggleOnBrowserItemsAreShownAndFamiliesAreDeduplicated()
    {
        var workspace = new Workspace
        {
            Items =
            [
                Browser(@"C:\Edge\msedge.exe", "Edge", toggle: true),
                Browser(@"C:\Edge\msedge.exe", "Edge second window", toggle: true),
                Browser(@"C:\Chrome\chrome.exe", "Chrome", toggle: false),
            ],
        };

        var notices = BrowserNoticeSelection.Unseen(workspace, _ => false);
        Assert.Single(notices);
        Assert.Equal("msedge", notices[0].Key);
    }

    [Fact]
    public void AcknowledgedKeysAreNotOfferedAgain()
    {
        var workspace = new Workspace
        {
            Items = [Browser(@"C:\Chrome\chrome.exe", "Chrome", toggle: true)],
        };

        Assert.Single(BrowserNoticeSelection.Unseen(workspace, _ => false));
        Assert.Empty(BrowserNoticeSelection.Unseen(workspace, key => key == "chrome"));
    }

    private static WorkspaceItem Browser(string exe, string name, bool toggle) => new()
    {
        Kind = ItemKind.App, Name = name, App = new AppRef { ExePath = exe }, ReopenLastTabs = toggle,
    };
}
