using FlowDesk.App.Services;
using FlowDesk.Core.Restore;

namespace FlowDesk.App.Tests;

public sealed class RestoreReportTextTests
{
    [Fact]
    public void SkippedIsInformationalAndShowsItsDetail()
    {
        var report = new RestoreReport(
        [
            new("edge", "Edge", ItemOutcome.Skipped, "The browser is already open, so its open windows keep their tabs"),
            new("code", "Code", ItemOutcome.Launched),
        ], TimeSpan.FromSeconds(1));

        var (message, hasFailures) = RestoreReportText.Summary("Study", report);
        Assert.False(hasFailures);
        Assert.Contains("1 of 2 windows in place", message);
        Assert.Contains("Skipped: Edge", message);
        Assert.Contains("already open", message);
        Assert.Equal("Skipped", RestoreReportText.Outcome(ItemOutcome.Skipped));
        Assert.Contains("already open", RestoreReportText.Detail(report.Items[0]));
    }

    [Fact]
    public void RealFailuresStillWarn()
    {
        var report = new RestoreReport(
        [
            new("discord", "Discord", ItemOutcome.LaunchFailed, "Program not found"),
            new("code", "Code", ItemOutcome.Launched),
        ], TimeSpan.FromSeconds(1));

        var (message, hasFailures) = RestoreReportText.Summary("Study", report);
        Assert.True(hasFailures);
        Assert.Contains("Discord", message);
        Assert.Contains("Program not found", message);
        Assert.Equal("Could not open", RestoreReportText.Outcome(ItemOutcome.LaunchFailed));
    }
}
