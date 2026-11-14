using System.Text.Json;
using FlowDesk.App.Services;
using FlowDesk.Core.Model;
using FlowDesk.Core.Windows;

namespace FlowDesk.App.Tests;

public sealed class UxDecisionTests
{
    [Theory]
    [InlineData(false, false, true, true, CloseAction.Hide)]
    [InlineData(true, false, true, true, CloseAction.Exit)]
    [InlineData(false, true, true, true, CloseAction.Exit)]
    [InlineData(false, false, false, true, CloseAction.Exit)]
    [InlineData(false, false, true, false, CloseAction.Exit)]
    public void CloseDistinguishesXFromExitAndNeverHidesDuringShutdown(bool exit, bool ending, bool loaded, bool tray, CloseAction expected) =>
        Assert.Equal(expected, UxDecisions.Close(exit, ending, loaded, tray));

    [Fact]
    public void FirstHideNoticeIsTakenOnceAndSurvivesSettingsRoundTrip()
    {
        var settings = new AppSettings();
        Assert.True(UxDecisions.TakeTrayNotice(settings));
        Assert.False(UxDecisions.TakeTrayNotice(settings));
        var reloaded = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        Assert.False(UxDecisions.TakeTrayNotice(reloaded));
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    public void SelectAllNeverSelectsUnavailableWindows(bool all, bool available, bool expected) =>
        Assert.Equal(expected, UxDecisions.SelectWindow(all, available));

    [Fact]
    public void SaveNeedsAnAvailableSelectedWindow()
    {
        Assert.False(UxDecisions.InitialWindowSelection);
        Assert.False(UxDecisions.CanSaveWindows([]));
        Assert.False(UxDecisions.CanSaveWindows([(false, true), (true, false)]));
        Assert.True(UxDecisions.CanSaveWindows([(true, true), (false, false)]));
    }

    [Fact]
    public void SelectAllThenNoneUpdatesSaveEligibility()
    {
        bool[] available = [true, false, true];
        var selected = available.Select(a => (UxDecisions.SelectWindow(true, a), a)).ToArray();
        Assert.True(UxDecisions.CanSaveWindows(selected));
        selected = available.Select(a => (UxDecisions.SelectWindow(false, a), a)).ToArray();
        Assert.False(UxDecisions.CanSaveWindows(selected));
    }

    [Theory]
    [InlineData(false, false, true, false)]
    [InlineData(true, false, true, true)]
    [InlineData(true, true, true, true)]
    [InlineData(true, false, false, false)]
    [InlineData(true, true, false, true)]
    public void ExplicitExitAsksForAnyRunningFocusNotOnlyStrict(bool session, bool strict, bool exit, bool expected) =>
        Assert.Equal(expected, UxDecisions.ConfirmFocusEnd(session, strict, exit));

    [Theory]
    [InlineData(false, false, 0, false)]
    [InlineData(false, false, 1, true)]
    [InlineData(false, true, 0, true)]
    [InlineData(true, false, 1, false)]
    [InlineData(true, true, 0, false)]
    public void FocusRequiresAnEffectiveRuleAndNoRunningSession(bool running, bool all, int count, bool expected) =>
        Assert.Equal(expected, UxDecisions.CanStartFocus(running, all, count));

    private static WindowInfo Window => new()
    {
        Handle = 12, ProcessId = 22, ExePath = "test.exe", Title = "Test", ClassName = "Test",
        MonitorDevice = "DISPLAY1", NormalRect = new(0, 0, 800, 600), Show = WindowShow.Normal,
        IsOnCurrentDesktop = true
    };

    [Fact]
    public void LayoutListsOnlyVisibleLocalWindowsOnChosenMonitorNotFlowDesk()
    {
        Assert.True(LayoutSelection.Includes(Window, "display1", 99));
        Assert.False(LayoutSelection.Includes(Window, "DISPLAY2", 99));
        Assert.False(LayoutSelection.Includes(Window, "DISPLAY1", 22));
        Assert.False(LayoutSelection.Includes(Window with { Show = WindowShow.Minimized }, "DISPLAY1", 99));
        Assert.False(LayoutSelection.Includes(Window with { IsOnCurrentDesktop = false, CloakReason = 2 }, "DISPLAY1", 99));
        Assert.False(LayoutSelection.Includes(Window with { IsOnCurrentDesktop = null, CloakReason = -1 }, "DISPLAY1", 99));
        Assert.True(LayoutSelection.Includes(Window with { IsOnCurrentDesktop = null, CloakReason = 0 }, "DISPLAY1", 99));
    }
}
