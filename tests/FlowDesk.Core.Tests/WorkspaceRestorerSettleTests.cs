using FlowDesk.Core.Model;
using FlowDesk.Core.Restore;
using FlowDesk.Core.Windows;

namespace FlowDesk.Core.Tests;

/// <summary>
/// Regression for the Chrome height drift: a launched browser may resize itself AFTER the first settle pass.
/// Only launched browsers get a second bounded pass; nothing loops forever, and a recycled handle is never moved.
/// </summary>
public class WorkspaceRestorerSettleTests
{
    private const string Chrome = @"C:\Program Files\Google\Chrome\Application\chrome.exe";
    private const string Notepad = @"C:\Windows\notepad.exe";
    private static readonly PixelRect Target = new(100, 100, 800, 600);

    [Fact]
    public async Task Browser_resize_after_first_settle_is_repaired_by_the_second_pass()
    {
        var inner = new FakeWindowSystem();
        var sys = new RecordingWindows(inner);
        var item = Build.App(Chrome, "Inbox - Google Chrome", Target);
        var launcher = new FakeLauncher(inner);
        launcher.Behaviour[item.Id] = (Chrome, "Inbox - Google Chrome", 1);
        bool secondSettleRan = false;

        var restorer = new WorkspaceRestorer(sys, launcher, new SessionTracker(), Delay(inner, sys, launcher, afterFirstSettle: () =>
        {
            secondSettleRan = true;
            Resize(inner, Chrome, Target with { Height = 822 }); // Chrome's own late resize
        }));

        var report = await restorer.RestoreAsync(Workspace(item), Settling());

        Assert.Equal(ItemOutcome.Launched, report.Items[0].Outcome);
        Assert.True(secondSettleRan);
        Assert.Equal(2, sys.PlaceCount); // initial placement + second settle repair
        var window = Assert.Single(inner.Windows);
        Assert.Equal(Target, window.NormalRect);
        Assert.Equal(Target, inner.Placed[^1].Rect);
    }

    [Fact]
    public async Task Non_browser_window_is_not_given_a_second_settle_pass()
    {
        var inner = new FakeWindowSystem();
        var sys = new RecordingWindows(inner);
        var item = Build.App(Notepad, "notes-a.txt - Notepad", Target);
        var launcher = new FakeLauncher(inner);
        launcher.Behaviour[item.Id] = (Notepad, "notes-a.txt - Notepad", 1);
        bool secondSettleRan = false;

        var restorer = new WorkspaceRestorer(sys, launcher, new SessionTracker(), Delay(inner, sys, launcher, afterFirstSettle: () =>
        {
            secondSettleRan = true;
            Resize(inner, Notepad, Target with { Height = 822 });
        }));

        var report = await restorer.RestoreAsync(Workspace(item), Settling());

        Assert.Equal(ItemOutcome.Launched, report.Items[0].Outcome);
        Assert.False(secondSettleRan);
        Assert.Equal(1, sys.PlaceCount);
        Assert.Equal(Target, Assert.Single(inner.Windows).NormalRect);
    }

    [Fact]
    public async Task Zero_settle_delay_does_no_extra_pass_even_for_a_browser()
    {
        var inner = new FakeWindowSystem();
        var sys = new RecordingWindows(inner);
        var item = Build.App(Chrome, "Inbox - Google Chrome", Target);
        var launcher = new FakeLauncher(inner);
        launcher.Behaviour[item.Id] = (Chrome, "Inbox - Google Chrome", 1);
        int delaysAfterPlacement = 0;

        var delay = (TimeSpan _, CancellationToken ct) =>
        {
            var d = launcher.Delay(TimeSpan.Zero, ct);
            if (sys.PlaceCount > 0) delaysAfterPlacement++;
            return d;
        };
        var restorer = new WorkspaceRestorer(sys, launcher, new SessionTracker(), delay);
        var options = new RestoreOptions
        {
            LaunchTimeout = TimeSpan.FromSeconds(20),
            PollInterval = TimeSpan.FromSeconds(1),
            SettleDelay = TimeSpan.Zero,
        };

        await restorer.RestoreAsync(Workspace(item), options);

        Assert.Equal(0, delaysAfterPlacement);
        Assert.Equal(1, sys.PlaceCount);
        Assert.Equal(Target, Assert.Single(inner.Windows).NormalRect);
    }

    [Fact]
    public async Task Recycled_handle_is_not_moved_by_either_settle_pass()
    {
        var inner = new FakeWindowSystem();
        var sys = new RecordingWindows(inner);
        var item = Build.App(Chrome, "Inbox - Google Chrome", Target);
        var launcher = new FakeLauncher(inner);
        launcher.Behaviour[item.Id] = (Chrome, "Inbox - Google Chrome", 1);
        int recycledPid = 0;
        bool recycled = false;

        var restorer = new WorkspaceRestorer(sys, launcher, new SessionTracker(), Delay(inner, sys, launcher, afterFirstSettle: () =>
        {
            if (recycled) return;
            recycled = true;
            var original = Assert.Single(inner.Windows);
            // The browser closed and Windows recycled the handle for a different process.
            recycledPid = original.ProcessId + 1000;
            inner.Windows[0] = original with
            {
                ProcessId = recycledPid,
                NormalRect = new PixelRect(5, 5, 300, 200),
                Show = WindowShow.Normal,
            };
        }));

        var report = await restorer.RestoreAsync(Workspace(item), Settling());

        Assert.Equal(ItemOutcome.Launched, report.Items[0].Outcome);
        Assert.True(recycled);
        Assert.Equal(1, sys.PlaceCount); // only the original placement, no settle move
        var window = Assert.Single(inner.Windows);
        Assert.Equal(new PixelRect(5, 5, 300, 200), window.NormalRect);
        Assert.Equal(recycledPid, window.ProcessId);
    }

    private static Workspace Workspace(WorkspaceItem item) => new() { Items = [item] };

    private static RestoreOptions Settling() => new()
    {
        LaunchTimeout = TimeSpan.FromSeconds(20),
        PollInterval = TimeSpan.FromSeconds(1),
        SettleDelay = TimeSpan.FromSeconds(1),
    };

    /// <summary>Runs the launcher's poll side effect, then invokes <paramref name="afterFirstSettle"/> on the next delay.</summary>
    private static Func<TimeSpan, CancellationToken, Task> Delay(
        FakeWindowSystem inner, RecordingWindows sys, FakeLauncher launcher, Action afterFirstSettle)
    {
        int postPlacementDelays = 0;
        return (TimeSpan _, CancellationToken ct) =>
        {
            var d = launcher.Delay(TimeSpan.Zero, ct);
            if (sys.PlaceCount == 0) return d; // the launch poll, before the first placement
            if (postPlacementDelays++ == 0) return d; // first settle pass: window is still in place
            afterFirstSettle();
            return d;
        };
    }

    private static void Resize(FakeWindowSystem sys, string exe, PixelRect rect)
    {
        int i = sys.Windows.FindIndex(w => w.ExePath == exe);
        Assert.True(i >= 0);
        sys.Windows[i] = sys.Windows[i] with { NormalRect = rect, Show = WindowShow.Normal };
    }

    private sealed class RecordingWindows(FakeWindowSystem inner) : IWindowSystem
    {
        public int PlaceCount { get; private set; }
        public int OwnProcessId => inner.OwnProcessId;

        public IReadOnlyList<WindowInfo> GetAppWindows() => inner.GetAppWindows();
        public WindowInfo? Get(long handle) => inner.Get(handle);
        public bool IsAlive(long handle) => inner.IsAlive(handle);
        public IReadOnlyList<MonitorInfo> GetMonitors() => inner.GetMonitors();

        public PlaceOutcome Place(long handle, PixelRect normalRect, WindowShow show)
        {
            PlaceCount++;
            return inner.Place(handle, normalRect, show);
        }

        public (int L, int T, int R, int B) GetInvisibleBorders(long handle) => inner.GetInvisibleBorders(handle);
        public void BringToFront(long handle) => inner.BringToFront(handle);
        public bool Activate(long handle) => inner.Activate(handle);
        public void Close(long handle) => inner.Close(handle);
        public void Minimize(long handle) => inner.Minimize(handle);
    }
}
