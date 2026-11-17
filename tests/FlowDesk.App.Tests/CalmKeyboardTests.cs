using System.Collections.ObjectModel;
using System.Xml.Linq;
using FlowDesk.App.Services;
using FlowDesk.App.ViewModels;
using FlowDesk.Core.Calm;

namespace FlowDesk.App.Tests;

public sealed class CalmKeyboardTests
{
    private static CalmReading Reading(int index, CalmState state = CalmState.Noisy) => new(CalmCatalog.All[index], state, null, true);
    private static StartupEntry App(string id, bool? enabled = true) => new(id, id, "app.exe", new("startup", id), enabled);

    [Fact] public void RefreshKeepsGroupsRowsCommandsAndCollectionsWithoutReset()
    {
        var groups = new ObservableCollection<CalmGroup>(); var apps = new ObservableCollection<CalmStartupRow>();
        int writes = 0; bool undo = false;
        Task Change(CalmRow _, bool __) { writes++; return Task.CompletedTask; }
        Task Startup(CalmStartupRow _, bool __) { writes++; return Task.CompletedTask; }
        void Refresh(CalmState state) => CalmRowRefresh.Apply(groups, apps, [Reading(0, state), Reading(1, state)],
            [App("one", state == CalmState.Noisy)], _ => undo, Change, Startup, () => true);
        Refresh(CalmState.Noisy);
        var group = groups[0]; var rows = group.Rows; var row = rows[0]; var app = apps[0];
        var change = row.ChangeCommand; var undoCommand = row.UndoCommand;
        int collectionChanges = 0;
        groups.CollectionChanged += (_, _) => collectionChanges++;
        rows.CollectionChanged += (_, _) => collectionChanges++;
        apps.CollectionChanged += (_, _) => collectionChanges++;
        undo = true; Refresh(CalmState.Calm);
        Assert.Same(group, groups[0]); Assert.Same(rows, groups[0].Rows); Assert.Same(row, rows[0]); Assert.Same(app, apps[0]);
        Assert.Same(change, row.ChangeCommand); Assert.Same(undoCommand, row.UndoCommand);
        Assert.False(row.IsOn); Assert.False(app.IsOn); Assert.True(row.HasUndo); Assert.True(app.HasUndo);
        Assert.Equal(0, writes); Assert.Equal(0, collectionChanges);
        undo = false; Refresh(CalmState.Noisy);
        Assert.True(row.IsOn); Assert.True(app.IsOn); Assert.False(row.HasUndo); Assert.False(app.HasUndo);
        Assert.Equal(0, writes); Assert.Equal(0, collectionChanges);
    }

    [Fact] public void StartupAddRemoveAndReorderKeepSurvivingRows()
    {
        var groups = new ObservableCollection<CalmGroup>(); var apps = new ObservableCollection<CalmStartupRow>();
        void Refresh(params StartupEntry[] fresh) => CalmRowRefresh.Apply(groups, apps, [Reading(0)], fresh,
            _ => false, (_, _) => Task.CompletedTask, (_, _) => Task.CompletedTask, () => true);
        Refresh(App("a"), App("b")); var a = apps[0]; var b = apps[1];
        Refresh(App("b"), App("c"), App("a")); Assert.Same(b, apps[0]); Assert.Same(a, apps[2]);
        Refresh(App("a", false)); Assert.Single(apps); Assert.Same(a, apps[0]); Assert.False(a.IsOn);
    }

    [Fact] public void BusyToggleRejectsInputWithoutDisablingOrWriting()
    {
        int writes = 0, notifications = 0; bool busy = true;
        var row = new CalmRow(Reading(0), false, (_, _) => { writes++; return Task.CompletedTask; }, () => !busy);
        var app = new CalmStartupRow(App("one"), false, (_, _) => { writes++; return Task.CompletedTask; }, () => !busy);
        row.PropertyChanged += (_, e) => { if (e.PropertyName == "IsOn") notifications++; };
        row.IsOn = false; app.IsOn = false;
        Assert.True(row.IsOn); Assert.True(app.IsOn); Assert.True(row.CanToggle); Assert.True(app.CanChange);
        Assert.Equal(0, writes); Assert.Equal(1, notifications);
        busy = false; row.IsOn = false; app.IsOn = false; Assert.Equal(2, writes);
    }

    [Fact] public async Task RunningRowCommandsRemainFocusableWhileOperationGuardOwnsSerialization()
    {
        var finish = new TaskCompletionSource();
        var row = new CalmRow(Reading(0), true, (_, _) => finish.Task);
        var run = row.UndoCommand.ExecuteAsync(null);
        Assert.True(row.UndoCommand.CanExecute(null));
        finish.SetResult(); await run;
    }

    [Fact] public void RefreshNotifiesAllBindingsWithoutWriteBack()
    {
        int writes = 0; var notifications = new List<string?>();
        var setting = CalmCatalog.All.Single(s => s.SearchChoice);
        var row = new CalmRow(new(setting, CalmState.Noisy, 2, true), false, (_, _) => { writes++; return Task.CompletedTask; });
        row.PropertyChanged += (_, e) => notifications.Add(e.PropertyName);
        row.Update(new(setting, CalmState.Managed, null, false), true);
        Assert.Contains(string.Empty, notifications); Assert.False(row.CanChange); Assert.True(row.HasUndo);
        Assert.Equal(-1, row.SelectedSearch); Assert.Equal(0, writes);
        Assert.Throws<ArgumentException>(() => row.Update(Reading(0), false));
    }

    [Fact] public void StructuralContainersAreNotTabStopsAndBusyDoesNotDisableFocusedSubtree()
    {
        var page = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", "CalmPage.xaml"));
        string[] containers = ["ScrollViewer", "ContentControl", "ItemsControl", "SettingsCard"];
        Assert.All(page.Descendants().Where(e => containers.Contains(e.Name.LocalName)),
            e => Assert.Equal("False", (string?)e.Attribute("IsTabStop")));
        Assert.DoesNotContain(page.Descendants().Attributes(), a => a.Value == "{Binding CanInteract}");
        var undos = page.Descendants().Where(e => e.Name.LocalName == "HyperlinkButton").ToArray();
        Assert.Equal(3, undos.Length); Assert.All(undos, e => Assert.NotNull(e.Attribute("Click")));
    }

    [Theory] [InlineData(true)] [InlineData(false)]
    public void CopiedNoticesExpireButWarningsDoNot(bool fallback)
    {
        var notice = PaletteNotice.Copied(fallback);
        Assert.True(notice.IsCopied); Assert.Equal(TimeSpan.FromSeconds(5), notice.DismissAfter);
        Assert.Equal(fallback ? "Copied. Press Ctrl+V to paste." : "Copied to clipboard.", notice.Message);
        var warning = new PaletteNotice("Clipboard is busy. Try again.");
        Assert.False(warning.IsCopied); Assert.Null(warning.DismissAfter);
    }

    [Fact] public async Task ExpiryDismissesOnlyItsOwnStatusOnUiQueue()
    {
        var lifetime = new StatusLifetime(); var timer = new TaskCompletionSource();
        Action? queued = null; int dismissals = 0;
        var expiry = lifetime.DismissAsync(lifetime.Generation, () => timer.Task, a => queued = a, () => dismissals++);
        timer.SetResult(); await expiry; Assert.Equal(0, dismissals);
        queued!(); Assert.Equal(1, dismissals);
    }

    [Fact] public async Task NewerStatusOrManualCloseInvalidatesEvenAlreadyQueuedExpiry()
    {
        var lifetime = new StatusLifetime(); Action? queued = null; int dismissals = 0;
        await lifetime.DismissAsync(lifetime.Generation, () => Task.CompletedTask, a => queued = a, () => dismissals++);
        lifetime.Invalidate(); queued!(); Assert.Equal(0, dismissals);
        await lifetime.DismissAsync(lifetime.Generation, () => Task.CompletedTask, a => queued = a, () => dismissals++);
        lifetime.Invalidate(); queued!(); Assert.Equal(0, dismissals);
    }
}
