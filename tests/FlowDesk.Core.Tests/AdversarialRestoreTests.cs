using FlowDesk.Core.Capture;
using FlowDesk.Core.Model;
using FlowDesk.Core.Restore;
using FlowDesk.Core.Windows;

namespace FlowDesk.Core.Tests;

public sealed class AdversarialRestoreTests
{
    [Fact]
    public async Task Reopening_active_session_keeps_live_edits_and_autosave_reads_them()
    {
        var sys = new FakeWindowSystem();
        var tracker = new SessionTracker();
        var launcher = new FakeLauncher(sys);
        var capture = new WorkspaceCapture(sys, tracker);
        var restorer = new WorkspaceRestorer(sys, launcher, tracker, launcher.Delay);
        var switcher = new WorkspaceSwitcher(sys, tracker, capture, restorer);
        var window = sys.Add("editor.exe", "Editor");
        var item = Build.App("editor.exe", "Editor");
        var a = new Workspace { Items = [item] };
        var b = new Workspace();
        await switcher.SwitchToAsync(a, null, options: Build.Fast());
        await switcher.SwitchToAsync(b, a, options: Build.Fast());
        await switcher.SwitchToAsync(a, b, options: Build.Fast());
        var edited = new PixelRect(240, 160, 700, 500);
        sys.Place(window.Handle, edited, WindowShow.Normal);
        await switcher.SwitchToAsync(a, a, options: Build.Fast());
        Assert.Equal(edited, sys.Get(window.Handle)!.NormalRect);
        capture.SaveLayout(a);
        Assert.Equal(edited, item.Window!.Rect);
        switcher.ResetSession(a.Id);
        sys.Place(window.Handle, new(10, 10, 400, 400), WindowShow.Normal);
        await switcher.SwitchToAsync(a, a, options: Build.Fast());
        Assert.Equal(edited, sys.Get(window.Handle)!.NormalRect); // explicit reset still uses saved layout
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void Capture_preserves_unknown_desktop_fields_but_accepts_known_updates(bool knownIndex, bool knownId)
    {
        var sys = new FakeWindowSystem();
        var tracker = new SessionTracker();
        var previousId = Guid.NewGuid();
        var newId = Guid.NewGuid();
        var item = Build.App("editor.exe", "Editor");
        item.Window!.DesktopIndex = 3; item.Window.DesktopId = previousId;
        var workspace = new Workspace { Items = [item] };
        var window = sys.Add("editor.exe", "Editor", desktopIndex: knownIndex ? 5 : null, desktopId: knownId ? newId : null);
        tracker.Bind(workspace.Id, item.Id, window);
        var capture = new WorkspaceCapture(sys, tracker);
        var snapshot = capture.SnapshotSession(workspace)[item.Id];
        capture.SaveLayout(workspace);
        foreach (var saved in new[] { snapshot, item.Window })
        {
            Assert.Equal(knownIndex ? 5 : 3, saved.DesktopIndex);
            Assert.Equal(knownId ? newId : previousId, saved.DesktopId);
        }
    }

    [Theory]
    [InlineData("gone")]
    [InlineData("pid")]
    [InlineData("app")]
    [InlineData("same")]
    public async Task Outgoing_identity_is_rechecked_after_restore(string change)
    {
        var sys = new FakeWindowSystem();
        var tracker = new SessionTracker();
        var launcher = new FakeLauncher(sys);
        var old = sys.Add("editor.exe", "Editor");
        var outgoing = Build.App("editor.exe", "Editor");
        var current = new Workspace { Items = [outgoing] };
        tracker.Bind(current.Id, outgoing.Id, old);
        var next = Build.App("next.exe", "Next");
        var target = new Workspace { Items = [next] };
        launcher.Behaviour[next.Id] = ("next.exe", "Next", 1);
        Task Delay(TimeSpan delay, CancellationToken ct)
        {
            sys.CloseWindow(old.Handle);
            if (change != "gone") sys.Windows.Add(old with
            {
                ProcessId = change == "pid" ? old.ProcessId + 1 : old.ProcessId,
                ExePath = change == "app" ? "unrelated.exe" : old.ExePath
            });
            return launcher.Delay(delay, ct);
        }
        var restorer = new WorkspaceRestorer(sys, launcher, tracker, Delay);
        var switcher = new WorkspaceSwitcher(sys, tracker, new WorkspaceCapture(sys, tracker), restorer);
        await switcher.SwitchToAsync(target, current, options: Build.Fast());
        Assert.Equal(change == "same", sys.Minimized.Contains(old.Handle));
    }
}
