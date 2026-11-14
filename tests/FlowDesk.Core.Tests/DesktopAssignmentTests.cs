using FlowDesk.Core.Capture;
using FlowDesk.Core.Desktops;
using FlowDesk.Core.Model;
using FlowDesk.Core.Restore;
using FlowDesk.Core.Windows;

namespace FlowDesk.Core.Tests;

/// <summary>
/// Same-title windows across desktops must be matched by their saved desktop before z-order,
/// both when restoring and when snapshotting/saving the layout. Pure fake data only.
/// </summary>
public class DesktopAssignmentTests
{
    private const string Paint = @"C:\apps\mspaint.exe";
    private const string Title = "Untitled - Paint";
    private static readonly PixelRect RectDesk0 = new(40, 60, 700, 500);
    private static readonly PixelRect RectDesk1 = new(900, 120, 640, 480);

    private readonly FakeWindowSystem _sys = new();

    [Fact]
    public async Task Restore_matches_two_same_title_windows_by_desktop_in_reverse_z_order()
    {
        var (w0, w1) = AddPaintWindows();
        var desktops = new FakeVirtualDesktops { Count = 2, Current = 0 };
        desktops.DesktopNumbers[w0.Handle] = 0;
        desktops.DesktopNumbers[w1.Handle] = 1;
        var (item0, item1) = PaintItems();
        var tracker = new SessionTracker();
        var restorer = new WorkspaceRestorer(_sys, new FakeLauncher(_sys), tracker, desktops: desktops);

        var report = await restorer.RestoreAsync(
            new Workspace { Id = "ws", Items = [item0, item1] }, Build.Fast());

        Assert.All(report.Items, r => Assert.Equal(ItemOutcome.Reused, r.Outcome));
        Assert.Empty(desktops.Moves); // correct assignment needs no desktop moves
        Assert.Empty(desktops.Gotos);
        Assert.Equal(w0.Handle, tracker.Live(_sys, "ws", item0.Id)!.Handle);
        Assert.Equal(w1.Handle, tracker.Live(_sys, "ws", item1.Id)!.Handle);
        Assert.Equal(RectDesk0, _sys.Get(w0.Handle)!.NormalRect);
        Assert.Equal(RectDesk1, _sys.Get(w1.Handle)!.NormalRect);
    }

    [Fact]
    public void Snapshot_fallback_matches_same_title_windows_by_desktop()
    {
        var (w0, w1) = AddPaintWindows();
        var (item0, item1) = PaintItems();
        var tracker = new SessionTracker();
        var capture = new WorkspaceCapture(_sys, tracker, new NoFolders());

        var snapshot = capture.SnapshotSession(new Workspace { Id = "ws", Items = [item0, item1] });

        Assert.Equal(RectDesk0, snapshot[item0.Id].Rect);
        Assert.Equal(0, snapshot[item0.Id].DesktopIndex);
        Assert.Equal(RectDesk1, snapshot[item1.Id].Rect);
        Assert.Equal(1, snapshot[item1.Id].DesktopIndex);
        Assert.Null(tracker.Live(_sys, "ws", item0.Id)); // snapshot still does not bind
        Assert.Null(tracker.Live(_sys, "ws", item1.Id));
    }

    [Fact]
    public void Save_layout_fallback_matches_same_title_windows_by_desktop_and_binds_them()
    {
        var (w0, w1) = AddPaintWindows();
        var (item0, item1) = PaintItems();
        var tracker = new SessionTracker();
        var capture = new WorkspaceCapture(_sys, tracker, new NoFolders());
        var ws = new Workspace { Id = "ws", Items = [item0, item1] };

        var updated = capture.SaveLayout(ws);

        Assert.Equal([item0.Id, item1.Id], updated);
        Assert.Equal(RectDesk0, item0.Window!.Rect);
        Assert.Equal(0, item0.Window.DesktopIndex);
        Assert.Equal(RectDesk1, item1.Window!.Rect);
        Assert.Equal(1, item1.Window.DesktopIndex);
        Assert.Equal(w0.Handle, tracker.Live(_sys, "ws", item0.Id)!.Handle);
        Assert.Equal(w1.Handle, tracker.Live(_sys, "ws", item1.Id)!.Handle);
    }

    [Fact]
    public void Desktop_preference_prefers_matching_guid_then_index_then_unknown()
    {
        var guid = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var other = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var window = W("Paint", desktopIndex: 0, desktopId: guid);

        Assert.Equal(8, WindowMatcher.DesktopPreference(window,
            new SavedWindow { DesktopId = guid, DesktopIndex = 1 }));
        Assert.Equal(4, WindowMatcher.DesktopPreference(window,
            new SavedWindow { DesktopIndex = 0 }));
        Assert.Equal(0, WindowMatcher.DesktopPreference(window, null));
        Assert.Equal(0, WindowMatcher.DesktopPreference(window,
            new SavedWindow { DesktopId = other, DesktopIndex = 1 }));
        Assert.Equal(0, WindowMatcher.DesktopPreference(W("Paint"), null));
    }

    [Fact]
    public void Exact_title_advantage_stays_stronger_than_the_maximum_desktop_preference()
    {
        var guid = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var item = Build.App(Paint, "Paint");
        var spec = MatchSpec.For(item);
        var exact = W("Paint");
        var containing = W("Paint - extra", desktopIndex: 0, desktopId: guid);
        var desktopOnly = new SavedWindow { DesktopId = guid, DesktopIndex = 0 };

        Assert.Equal(30, WindowMatcher.TitleSimilarity(exact.Title, "Paint"));
        Assert.Equal(20, WindowMatcher.TitleSimilarity(containing.Title, "Paint"));
        Assert.True(WindowMatcher.Score(exact, spec) + WindowMatcher.DesktopPreference(exact, null)
            > WindowMatcher.Score(containing, spec) + WindowMatcher.DesktopPreference(containing, desktopOnly));
    }

    private (WindowInfo Desk0, WindowInfo Desk1) AddPaintWindows()
    {
        var desk1 = _sys.Add(Paint, Title, desktopIndex: 1, isOnCurrentDesktop: false);
        var desk0 = _sys.Add(Paint, Title, desktopIndex: 0, isOnCurrentDesktop: false);
        int i1 = _sys.Windows.FindIndex(w => w.Handle == desk1.Handle);
        int i0 = _sys.Windows.FindIndex(w => w.Handle == desk0.Handle);
        _sys.Windows[i1] = _sys.Windows[i1] with { NormalRect = RectDesk1 };
        _sys.Windows[i0] = _sys.Windows[i0] with { NormalRect = RectDesk0 };
        return (_sys.Get(desk0.Handle)!, _sys.Get(desk1.Handle)!);
    }

    private static (WorkspaceItem Desk0, WorkspaceItem Desk1) PaintItems()
    {
        var item0 = Build.App(Paint, Title, RectDesk0);
        item0.Window!.DesktopIndex = 0;
        var item1 = Build.App(Paint, Title, RectDesk1);
        item1.Window!.DesktopIndex = 1;
        return (item0, item1);
    }

    private static WindowInfo W(string title, int? desktopIndex = null, Guid? desktopId = null) => new()
    {
        Handle = 500 + title.Length, ProcessId = 7, ExePath = Paint, Title = title, ClassName = "MSPaintApp",
        NormalRect = new(0, 0, 100, 100), Show = WindowShow.Normal, DesktopIndex = desktopIndex, DesktopId = desktopId,
    };

    private sealed class NoFolders : IFolderPathSource
    {
        public IReadOnlyDictionary<long, string> GetOpenFolders() => new Dictionary<long, string>();
    }
}
