using FlowDesk.Core.Capture;
using FlowDesk.Core.Model;
using FlowDesk.Core.Restore;
using FlowDesk.Core.Windows;

namespace FlowDesk.Core.Tests;

/// <summary>
/// Regression for the real Paint swap: "Save open windows" bound an old handle, the app was closed and
/// launched again, and another workspace owned a live same-title window. Capture matching must prefer the
/// unbound replacement (like the restorer's ownership bonus) instead of taking another workspace's window.
/// </summary>
public class WorkspaceCaptureOwnershipTests
{
    private const string PaintExe = @"C:\apps\mspaint.exe";
    private const string PaintTitle = "Untitled - Paint";

    private readonly FakeWindowSystem _sys = new();
    private readonly SessionTracker _tracker = new();
    private readonly WorkspaceCapture _capture;

    public WorkspaceCaptureOwnershipTests()
    {
        _capture = new WorkspaceCapture(_sys, _tracker, new NoFolders());
    }

    [Fact]
    public void SnapshotSession_uses_the_unbound_replacement_not_another_workspaces_paint()
    {
        var (a, _, otherPaint, replacement) = ArrangePaintOwnership();
        _sys.Place(otherPaint.Handle, new PixelRect(100, 100, 900, 700), WindowShow.Normal);
        _sys.Place(replacement.Handle, new PixelRect(700, 300, 880, 625), WindowShow.Normal);

        var snapshot = _capture.SnapshotSession(a);

        var saved = Assert.Contains(a.Items[0].Id, snapshot);
        Assert.Equal(new PixelRect(700, 300, 880, 625), saved.Rect);
        Assert.Equal(WindowShow.Normal, saved.Show);
        // SnapshotSession must not bind (the next Open still owns the tracker), but it must not choose
        // the frontmost same-title window that belongs to the other workspace either.
        Assert.Null(_tracker.Live(_sys, a.Id, a.Items[0].Id));
    }

    [Fact]
    public void SaveLayout_binds_the_unbound_replacement_not_another_workspaces_paint()
    {
        var (a, _, otherPaint, replacement) = ArrangePaintOwnership();
        _sys.Place(otherPaint.Handle, new PixelRect(100, 100, 900, 700), WindowShow.Normal);
        _sys.Place(replacement.Handle, new PixelRect(700, 300, 880, 625), WindowShow.Normal);

        var updated = _capture.SaveLayout(a);

        Assert.Equal([a.Items[0].Id], updated);
        Assert.Equal(new PixelRect(700, 300, 880, 625), a.Items[0].Window!.Rect);
        var live = _tracker.Live(_sys, a.Id, a.Items[0].Id);
        Assert.NotNull(live);
        Assert.Equal(replacement.Handle, live!.Handle);
        Assert.Equal(replacement.ProcessId, live.ProcessId);
        Assert.NotEqual(otherPaint.Handle, live.Handle);
    }

    [Fact]
    public void Shared_singleton_still_falls_back_when_no_unbound_window_exists()
    {
        var a = new Workspace { Id = "a", Items = [Build.App(PaintExe, PaintTitle, new(10, 10, 880, 625))] };
        var otherPaint = _sys.Add(PaintExe, PaintTitle);
        _tracker.Bind("other", "paint", otherPaint);

        var snapshot = _capture.SnapshotSession(a);
        var updated = _capture.SaveLayout(a);

        Assert.Equal(new PixelRect(10, 10, 800, 600), snapshot[a.Items[0].Id].Rect);
        Assert.Equal([a.Items[0].Id], updated);
        var live = _tracker.Live(_sys, a.Id, a.Items[0].Id);
        Assert.NotNull(live);
        Assert.Equal(otherPaint.Handle, live!.Handle); // no ownership veto for a true singleton
    }

    /// <summary>
    /// Paint A's saved handle is closed; another workspace owns the live Paint that is frontmost;
    /// the replacement (second in z-order) is unbound and must win.
    /// </summary>
    private (Workspace A, Workspace Other, WindowInfo OtherPaint, WindowInfo Replacement) ArrangePaintOwnership()
    {
        var oldPaint = _sys.Add(PaintExe, PaintTitle, pid: 11);
        var otherPaint = _sys.Add(PaintExe, PaintTitle, pid: 22);
        var replacement = _sys.Add(PaintExe, PaintTitle, pid: 33);

        var a = new Workspace { Id = "a", Items = [Build.App(PaintExe, PaintTitle, new(720, 120, 880, 625))] };
        var other = new Workspace { Id = "other", Items = [Build.App(PaintExe, PaintTitle, new(100, 100, 900, 700))] };
        _tracker.Bind(a.Id, a.Items[0].Id, oldPaint);
        _tracker.Bind(other.Id, other.Items[0].Id, otherPaint);
        _sys.CloseWindow(oldPaint.Handle); // Paint was closed; the handle is dead and the binding must drop

        return (a, other, otherPaint, replacement);
    }

    private sealed class NoFolders : IFolderPathSource
    {
        public IReadOnlyDictionary<long, string> GetOpenFolders() => new Dictionary<long, string>();
    }
}
