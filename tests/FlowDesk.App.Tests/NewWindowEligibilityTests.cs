using FlowDesk.App.Services;
using FlowDesk.Core.Model;
using FlowDesk.Core.Windows;

namespace FlowDesk.App.Tests;

public sealed class NewWindowEligibilityTests
{
    [Fact]
    public void TwoWindowsMatchingOneMissingItemBothCountAsReplacements()
    {
        var workspace = new Workspace
        {
            Items =
            [
                new WorkspaceItem
                {
                    Id = "note", Kind = ItemKind.App, Name = "Notepad",
                    App = new AppRef { ExePath = @"C:\Windows\notepad.exe" },
                    Window = new SavedWindow { Title = "note - Notepad" },
                },
            ],
        };
        var first = Window(1, "note - Notepad");
        var second = Window(2, "note - Notepad");

        Assert.True(NewWindowEligibility.MatchesMissingLiveItem(first, workspace, new Dictionary<string, WindowInfo>()));
        Assert.True(NewWindowEligibility.MatchesMissingLiveItem(second, workspace, new Dictionary<string, WindowInfo>()));
    }

    [Fact]
    public void ItemWithALiveBoundWindowDoesNotMakeOtherWindowsReplacements()
    {
        var workspace = new Workspace
        {
            Items =
            [
                new WorkspaceItem
                {
                    Id = "note", Kind = ItemKind.App, Name = "Notepad",
                    App = new AppRef { ExePath = @"C:\Windows\notepad.exe" },
                },
            ],
        };
        var bound = new Dictionary<string, WindowInfo> { ["note"] = Window(1, "note - Notepad") };

        Assert.False(NewWindowEligibility.MatchesMissingLiveItem(Window(2, "other - Notepad"), workspace, bound));
    }

    [Fact]
    public void DifferentBrowserProfileIsNotAReplacement()
    {
        var workspace = new Workspace
        {
            Items =
            [
                new WorkspaceItem
                {
                    Id = "work", Kind = ItemKind.App, Name = "Chrome",
                    App = new AppRef { ExePath = @"C:\Chrome\chrome.exe", Arguments = "--user-data-dir=C:\\Profiles\\Work" },
                    Window = new SavedWindow { Title = "Docs" },
                },
            ],
        };
        var otherProfile = Window(3, "Docs", exe: @"C:\Chrome\chrome.exe", args: "--user-data-dir=C:\\Profiles\\Personal");

        Assert.False(NewWindowEligibility.MatchesMissingLiveItem(otherProfile, workspace, new Dictionary<string, WindowInfo>()));
    }

    private static WindowInfo Window(long handle, string title, string exe = @"C:\Windows\notepad.exe", string? args = null) => new()
    {
        Handle = handle, ProcessId = (int)handle + 100, ExePath = exe, Arguments = args, Title = title,
        ClassName = "Notepad", NormalRect = new(0, 0, 800, 600), Show = WindowShow.Normal,
    };
}
