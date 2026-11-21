using FlowDesk.App.Services;
using FlowDesk.Core.Model;

namespace FlowDesk.App.Tests;

public sealed class WorkspaceUxTests
{
    [Fact] public void DefaultNameFillsFirstHoleAndDuplicateWarningIgnoresCaseAndWhitespace()
    {
        Assert.Equal("Workspace 2", UxDecisions.NextWorkspaceName(["Workspace 1", "workspace 3", "Workspace 4"]));
        Assert.Equal("Workspace 1", UxDecisions.NextWorkspaceName([]));
        Assert.True(UxDecisions.DuplicateWorkspaceName(" WORK ", ["Work"]));
        Assert.False(UxDecisions.DuplicateWorkspaceName(" ", [" "]));
    }
    [Fact] public void DeleteUndoRestoresSnapshotLayoutAndIndexOnlyOnce()
    {
        var undo = new WorkspaceDeleteUndo(); var now = DateTimeOffset.UtcNow;
        var original = new Workspace { Name = "Work", Items = [new() { Window = new() { MonitorStableId = "panel", Rect = new(1, 2, 3, 4) } }] };
        undo.Arm(original, 3, now); original.Name = "Mutated"; original.Items[0].Window!.MonitorStableId = "other";
        var saved = undo.Take(now.AddSeconds(9))!;
        Assert.Equal("Work", saved.Workspace.Name); Assert.Equal(3, saved.Index);
        Assert.Equal("panel", saved.Workspace.Items[0].Window!.MonitorStableId);
        Assert.Null(undo.Take(now));
    }
    [Fact] public void UndoExpiresOnChangeOrTimeoutAndOldTimerCannotExpireNewDelete()
    {
        var undo = new WorkspaceDeleteUndo(); var now = DateTimeOffset.UtcNow;
        var old = undo.Arm(new(), 0, now);
        undo.Invalidate(); Assert.Null(undo.Take(now));
        undo.Arm(new(), 1, now); Assert.False(undo.Expire(old));
        Assert.Null(undo.Take(now.AddSeconds(10)));
    }
    [Fact] public void ItemEditKeepsIdentityPlacementAndBrowserPreferencesWithoutMutatingOriginal()
    {
        var item = new WorkspaceItem { Kind = ItemKind.Website, Name = "Old", Urls = ["https://old.test"],
            ReopenLastTabs = false, Window = new() { MonitorStableId = "panel" } };
        Assert.Null(WorkspaceItemEdits.Apply(item, " New ", "https://new.test\nhttps://new.test", "", _ => true, _ => true, out var edited));
        Assert.Equal(item.Id, edited.Id); Assert.Equal("New", edited.Name);
        Assert.Equal(["https://new.test"], edited.Urls); Assert.Equal("panel", edited.Window!.MonitorStableId);
        Assert.False(edited.ReopenLastTabs); Assert.Equal("Old", item.Name); Assert.Equal(["https://old.test"], item.Urls);
        Assert.NotNull(WorkspaceItemEdits.Apply(item, "New", "javascript:bad", "", _ => true, _ => true, out _));
    }
    [Fact] public void PathsMustExistAndArgumentsChangeOnlyTheClone()
    {
        var file = new WorkspaceItem { Kind = ItemKind.File };
        Assert.NotNull(WorkspaceItemEdits.Apply(file, "File", "missing", "", _ => false, _ => true, out _));
        file.Kind = ItemKind.Folder;
        Assert.Null(WorkspaceItemEdits.Apply(file, "Folder", "path", "", _ => false, _ => true, out var folder));
        Assert.Equal("path", folder.Path);
        var app = new WorkspaceItem { App = new() { ExePath = "app.exe", Arguments = "old" } };
        Assert.Null(WorkspaceItemEdits.Apply(app, "App", "ignored", "new", _ => false, _ => false, out var edited));
        Assert.Equal("new", edited.App!.Arguments); Assert.Equal("old", app.App.Arguments);
        Assert.Equal("app.exe", edited.App.ExePath);
    }
}
