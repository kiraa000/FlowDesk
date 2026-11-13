using FlowDesk.App.Services;
using FlowDesk.Core.Model;
using FlowDesk.Core.Restore;
using FlowDesk.Core.Windows;

namespace FlowDesk.App.Tests;

public sealed class SaveOpenWindowsBindingTests
{
    [Fact]
    public void SavedAppWindow_IsBoundToTheNewWorkspaceAndItsItem()
    {
        var windows = new FakeWindows();
        var tracker = new SessionTracker();
        var window = windows.Add(@"C:\apps\notepad.exe", "notes-a.txt - Notepad", pid: 1234);
        var item = new WorkspaceItem { Kind = ItemKind.App, Name = "Notepad", App = new AppRef { ExePath = @"C:\apps\notepad.exe" } };
        var workspace = new Workspace { Id = "rt-work-a", Items = [item] };

        int bound = SaveOpenWindowsBinding.Bind(tracker, workspace, [(item, window)]);

        Assert.Equal(1, bound);
        var live = tracker.Live(windows, workspace.Id, item.Id);
        Assert.NotNull(live);
        Assert.Equal(window.Handle, live!.Handle);
        Assert.Equal(window.ProcessId, live.ProcessId);
    }

    [Fact]
    public void CapturedBrowserAppItem_IsBound()
    {
        var windows = new FakeWindows();
        var tracker = new SessionTracker();
        var window = windows.Add(@"C:\Program Files\Google\Chrome\Application\chrome.exe", "Inbox - Google Chrome", pid: 555);
        var item = new WorkspaceItem
        {
            Kind = ItemKind.App, Name = "Google Chrome", ReopenLastTabs = true,
            App = new AppRef
            {
                ExePath = @"C:\Program Files\Google\Chrome\Application\chrome.exe",
                Arguments = @"--user-data-dir=D:\Temp\chrome-profile",
            },
        };
        var workspace = new Workspace { Id = "rt-work-a", Items = [item] };

        int bound = SaveOpenWindowsBinding.Bind(tracker, workspace, [(item, window)]);

        Assert.Equal(1, bound);
        Assert.Equal(window.Handle, tracker.Live(windows, workspace.Id, item.Id)!.Handle);
    }

    [Fact]
    public void WebsiteItem_IsNotBound_SoItsLinksStillOpen()
    {
        var windows = new FakeWindows();
        var tracker = new SessionTracker();
        var window = windows.Add(@"C:\Chrome\chrome.exe", "Example - Google Chrome", pid: 77);
        var item = new WorkspaceItem
        {
            Kind = ItemKind.Website, Name = "Docs", Urls = ["https://example.com"],
            App = new AppRef { ExePath = @"C:\Chrome\chrome.exe" },
        };
        var workspace = new Workspace { Id = "rt-work-a", Items = [item] };

        int bound = SaveOpenWindowsBinding.Bind(tracker, workspace, [(item, window)]);

        Assert.Equal(0, bound);
        Assert.Null(tracker.Live(windows, workspace.Id, item.Id));
    }

    [Fact]
    public void TwoSavedWindows_KeepTheirOwnItemBindings()
    {
        var windows = new FakeWindows();
        var tracker = new SessionTracker();
        var paintA = windows.Add(@"C:\apps\mspaint.exe", "Untitled - Paint", pid: 11);
        var paintB = windows.Add(@"C:\apps\mspaint.exe", "Untitled - Paint", pid: 22);
        var itemA = new WorkspaceItem { Kind = ItemKind.App, Name = "Paint A", App = new AppRef { ExePath = @"C:\apps\mspaint.exe" } };
        var itemB = new WorkspaceItem { Kind = ItemKind.App, Name = "Paint B", App = new AppRef { ExePath = @"C:\apps\mspaint.exe" } };
        var workspace = new Workspace { Id = "rt-work-a", Items = [itemA, itemB] };

        SaveOpenWindowsBinding.Bind(tracker, workspace, [(itemA, paintA), (itemB, paintB)]);

        Assert.Equal(paintA.Handle, tracker.Live(windows, workspace.Id, itemA.Id)!.Handle);
        Assert.Equal(paintB.Handle, tracker.Live(windows, workspace.Id, itemB.Id)!.Handle);
    }

    private sealed class FakeWindows : IWindowSystem
    {
        private readonly List<WindowInfo> _windows = [];
        public int OwnProcessId => 1;

        public WindowInfo Add(string exe, string title, int pid)
        {
            var window = new WindowInfo
            {
                Handle = 100 + _windows.Count, ProcessId = pid, ExePath = exe, Title = title,
                ClassName = "Win", NormalRect = new(0, 0, 800, 600), Show = WindowShow.Normal,
            };
            _windows.Add(window);
            return window;
        }

        public IReadOnlyList<WindowInfo> GetAppWindows() => _windows;
        public WindowInfo? Get(long handle) => _windows.FirstOrDefault(w => w.Handle == handle);
        public bool IsAlive(long handle) => _windows.Any(w => w.Handle == handle);
        public IReadOnlyList<MonitorInfo> GetMonitors() => [];
        public PlaceOutcome Place(long handle, PixelRect normalRect, WindowShow show) => PlaceOutcome.Ok;
        public (int L, int T, int R, int B) GetInvisibleBorders(long handle) => (0, 0, 0, 0);
        public void BringToFront(long handle) { }
        public bool Activate(long handle) => true;
        public void Close(long handle) { }
        public void Minimize(long handle) { }
    }
}
