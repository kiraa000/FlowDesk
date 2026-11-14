using FlowDesk.Core.Capture;
using FlowDesk.Core.Model;
using FlowDesk.Core.Restore;
using FlowDesk.Core.Storage;
using FlowDesk.Core.Windows;

namespace FlowDesk.Core.Tests;

/// <summary>
/// Capture must list windows from every virtual desktop and persist each window's desktop.
/// Snapshots must follow a window that the user moved to another desktop.
/// </summary>
public class DesktopCaptureTests
{
    private const string Code = @"C:\apps\code.exe";
    private const string Spotify = @"C:\apps\spotify.exe";
    private static readonly Guid DesktopA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid DesktopB = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

    private readonly FakeWindowSystem _sys = new();
    private readonly SessionTracker _tracker = new();
    private readonly WorkspaceCapture _capture;

    public DesktopCaptureTests() => _capture = new WorkspaceCapture(_sys, _tracker, new NoFolders());

    [Fact]
    public void Capture_lists_windows_from_other_desktops_and_saves_their_desktop()
    {
        _sys.Add(Code, "proj - Visual Studio Code", desktopIndex: 0, desktopId: DesktopA, isOnCurrentDesktop: true);
        _sys.Add(Spotify, "Spotify", desktopIndex: 2, desktopId: DesktopB, isOnCurrentDesktop: false, cloakReason: 2);

        var candidates = _capture.CaptureOpenWindows();

        Assert.Equal(2, candidates.Count);
        var other = Assert.Single(candidates, c => c.Item.App?.ExePath == Spotify);
        Assert.Equal(2, other.Item.Window!.DesktopIndex);
        Assert.Equal(DesktopB, other.Item.Window.DesktopId);
        Assert.Equal(0, candidates.Single(c => c.Item.App?.ExePath == Code).Item.Window!.DesktopIndex);
    }

    [Fact]
    public void ToSaved_copies_desktop_metadata()
    {
        var w = _sys.Add(Spotify, "Spotify", desktopIndex: 3, desktopId: DesktopB, isOnCurrentDesktop: false);

        var saved = WorkspaceCapture.ToSaved(w, 4, _sys.GetMonitors());

        Assert.Equal(3, saved.DesktopIndex);
        Assert.Equal(DesktopB, saved.DesktopId);
    }

    [Fact]
    public void Snapshot_follows_a_window_moved_to_another_desktop()
    {
        var w = _sys.Add(Code, "proj - Visual Studio Code", desktopIndex: 0, desktopId: DesktopA, isOnCurrentDesktop: true);
        var item = Build.App(Code, "proj - Visual Studio Code");
        var ws = new Workspace { Id = "ws", Items = [item] };
        _tracker.Bind(ws.Id, item.Id, w);

        var before = _capture.SnapshotSession(ws);
        Assert.Equal(0, before[item.Id].DesktopIndex);

        // The user moves the window to desktop 2 and the next snapshot must keep that.
        _sys.Windows[0] = w with { DesktopIndex = 2, DesktopId = DesktopB, IsOnCurrentDesktop = false };
        var after = _capture.SnapshotSession(ws);

        Assert.Equal(2, after[item.Id].DesktopIndex);
        Assert.Equal(DesktopB, after[item.Id].DesktopId);
    }

    [Fact]
    public void SaveLayout_updates_the_saved_desktop_after_a_move()
    {
        var w = _sys.Add(Code, "proj - Visual Studio Code", desktopIndex: 0, desktopId: DesktopA, isOnCurrentDesktop: true);
        var item = Build.App(Code, "proj - Visual Studio Code");
        var ws = new Workspace { Id = "ws", Items = [item] };
        _tracker.Bind(ws.Id, item.Id, w);
        _sys.Windows[0] = w with { DesktopIndex = 1, DesktopId = DesktopB, IsOnCurrentDesktop = false };

        var updated = _capture.SaveLayout(ws);

        Assert.Equal([item.Id], updated);
        Assert.Equal(1, item.Window!.DesktopIndex);
        Assert.Equal(DesktopB, item.Window.DesktopId);
    }

    [Fact]
    public async Task Old_json_without_desktop_fields_loads_and_new_json_round_trips_them()
    {
        string dir = Path.Combine(Path.GetTempPath(), "fd-desktops-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            const string oldJson = """
            {
              "Version": 1,
              "Workspaces": [
                {
                  "Id": "old",
                  "Name": "Old",
                  "Items": [
                    {
                      "Id": "item",
                      "Kind": "App",
                      "Name": "Notepad",
                      "App": { "ExePath": "C:\\Windows\\notepad.exe" },
                      "Window": {
                        "Rect": { "X": 10, "Y": 10, "Width": 800, "Height": 600 },
                        "Show": "Normal",
                        "MonitorDevice": "\\\\.\\DISPLAY1",
                        "MonitorBounds": { "X": 0, "Y": 0, "Width": 1920, "Height": 1080 },
                        "MonitorWorkArea": { "X": 0, "Y": 0, "Width": 1920, "Height": 1040 },
                        "Title": "notes.txt - Notepad",
                        "ClassName": "Notepad",
                        "ZOrder": 0
                      }
                    }
                  ]
                }
              ]
            }
            """;
            File.WriteAllText(Path.Combine(dir, "flowdesk.json"), oldJson);
            var store = new JsonStore(dir);

            var loaded = store.Load(out var recovered);

            Assert.Null(recovered);
            var saved = loaded.Workspaces[0].Items[0].Window!;
            Assert.Null(saved.DesktopIndex);
            Assert.Null(saved.DesktopId);

            saved.DesktopIndex = 2;
            saved.DesktopId = DesktopB;
            await store.SaveAsync(loaded);
            var again = store.Load(out _).Workspaces[0].Items[0].Window!;
            Assert.Equal(2, again.DesktopIndex);
            Assert.Equal(DesktopB, again.DesktopId);
        }
        finally { Directory.Delete(dir, true); }
    }

    private sealed class NoFolders : IFolderPathSource
    {
        public IReadOnlyDictionary<long, string> GetOpenFolders() => new Dictionary<long, string>();
    }
}
