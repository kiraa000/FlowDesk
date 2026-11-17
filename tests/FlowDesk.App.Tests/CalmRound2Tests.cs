using FlowDesk.App.Services;
using FlowDesk.App.ViewModels;
using FlowDesk.Core.Calm;
using System.Xml.Linq;

namespace FlowDesk.App.Tests;

public sealed class CalmRound2Tests
{
    [Fact] public async Task AutoRestartShellDoesNotNeedAnotherExplorerLaunch()
    {
        uint?[] sequence = [10, null, null, 11]; int reads = 0; var delays = new List<TimeSpan>();
        Assert.True(await ExplorerRestartWait.WaitAsync(10, () => sequence[reads++], delay => { delays.Add(delay); return Task.CompletedTask; }));
        Assert.Equal(4, reads); Assert.Equal(3, delays.Count); Assert.All(delays, d => Assert.Equal(TimeSpan.FromMilliseconds(250), d));
    }
    [Fact] public async Task MissingShellTimesOutAfterSixSeconds()
    {
        TimeSpan elapsed = TimeSpan.Zero;
        Assert.False(await ExplorerRestartWait.WaitAsync(10, () => null, delay => { elapsed += delay; return Task.CompletedTask; }));
        Assert.Equal(TimeSpan.FromSeconds(6), elapsed);
    }
    [Fact] public async Task OldShellIsNotMistakenForReplacement()
    {
        int delays = 0;
        Assert.False(await ExplorerRestartWait.WaitAsync(10, () => 10, _ => { delays++; return Task.CompletedTask; }));
        Assert.Equal(24, delays);
    }
    [Fact] public void ToggleInitializationAndSameValueNeverWrite()
    {
        int changes = 0;
        var row = new CalmRow(new(CalmCatalog.All[0], CalmState.Noisy, null, true), true, (_, _) => { changes++; return Task.CompletedTask; });
        Assert.True(row.IsOn); row.IsOn = true; Assert.Equal(0, changes);
        row.IsOn = false; Assert.Equal(1, changes);
        Assert.Equal("Undo " + row.Title, row.UndoName);
    }
    [Theory] [InlineData(CalmState.Custom)] [InlineData(CalmState.Managed)] [InlineData(CalmState.Unavailable)]
    public void UneditableRowsNeverWriteFromToggle(CalmState state)
    {
        int changes = 0;
        var row = new CalmRow(new(CalmCatalog.All[0], state, null, true), false, (_, _) => { changes++; return Task.CompletedTask; });
        Assert.False(row.CanToggle); row.IsOn = true; Assert.Equal(0, changes);
        Assert.DoesNotContain("Windows default", row.Caption);
        if (state == CalmState.Managed) Assert.Equal("Set by a policy on this PC", row.Caption);
        if (state == CalmState.Custom) Assert.Equal("Custom value — not changed by Calm all", row.Caption);
    }
    [Fact] public async Task SearchSelectionDoesNotWriteUntilApply()
    {
        int changes = 0;
        var setting = CalmCatalog.All.Single(s => s.SearchChoice);
        var row = new CalmRow(new(setting, CalmState.Custom, null, true), true, (_, _) => { changes++; return Task.CompletedTask; });
        Assert.False(row.CanApplySearch); Assert.False(row.CanToggle);
        row.SelectedSearch = 1; Assert.True(row.CanApplySearch); Assert.Equal(0, changes);
        await row.ChangeCommand.ExecuteAsync(null); Assert.Equal(1, changes);
    }
    [Fact] public async Task FirstVisitReadsOncePerRunAndDoesNotReplayOffer()
    {
        var session = new CalmScanSession(); bool hasScan = false; int scans = 0;
        Task Scan() { scans++; hasScan = true; return Task.CompletedTask; }
        await session.EnsureAsync(() => hasScan, Scan); await session.EnsureAsync(() => hasScan, Scan);
        Assert.Equal(1, scans);
        // A new VM/app run has no in-memory snapshot, independently of the persisted first-offer flag.
        hasScan = false; await new CalmScanSession().EnsureAsync(() => hasScan, Scan); Assert.Equal(2, scans);
    }
    [Fact] public async Task ConcurrentVisitsShareReadAndFailedReadsCanRetry()
    {
        var session = new CalmScanSession(); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool hasScan = false; int scans = 0;
        async Task Scan() { scans++; await release.Task; hasScan = true; }
        var first = session.EnsureAsync(() => hasScan, Scan);
        var second = session.EnsureAsync(() => hasScan, Scan);
        release.SetResult(); await Task.WhenAll(first, second); Assert.Equal(1, scans);
        hasScan = false;
        await Assert.ThrowsAsync<IOException>(() => session.EnsureAsync(() => hasScan, () => throw new IOException()));
        await session.EnsureAsync(() => hasScan, Scan); Assert.Equal(2, scans);
    }
    [Fact] public void SettingsCardsAreAdaptiveAndActionsHaveDistinctAccessibleNames()
    {
        var document = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", "CalmPage.xaml"));
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace toolkit = "using:CommunityToolkit.WinUI.Controls";
        Assert.All(document.Descendants(toolkit + "SettingsCard"), card => Assert.NotEqual("Vertical", (string?)card.Attribute("ContentAlignment")));
        var toggles = document.Descendants(xaml + "ToggleSwitch").ToArray(); Assert.Equal(2, toggles.Length);
        Assert.All(toggles, toggle => Assert.NotNull(toggle.Attribute("AutomationProperties.Name")));
        var undo = document.Descendants(xaml + "HyperlinkButton").Where(b => (string?)b.Attribute("Content") == "Undo").ToArray();
        Assert.Equal(2, undo.Length); Assert.All(undo, b => Assert.Equal("{Binding UndoName}", (string?)b.Attribute("AutomationProperties.Name")));
        var apply = document.Descendants(xaml + "Button").Single(b => (string?)b.Attribute("Content") == "Apply");
        Assert.Equal("Apply search choice", (string?)apply.Attribute("AutomationProperties.Name"));
        var search = document.Descendants(xaml + "ComboBox").Single(); Assert.Null(search.Attribute("SelectionChanged"));
        Assert.Equal("Search on the taskbar", (string?)search.Attribute("AutomationProperties.Name"));
        Assert.All(document.Descendants(xaml + "TextBlock"), text =>
        {
            Assert.Null(text.Attribute("FontSize"));
            Assert.StartsWith("{ThemeResource ", (string?)text.Attribute("Foreground"));
            Assert.Equal("Wrap", (string?)text.Attribute("TextWrapping") ?? "Wrap");
        });
    }
}
