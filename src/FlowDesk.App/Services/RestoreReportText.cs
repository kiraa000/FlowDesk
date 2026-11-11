using FlowDesk.Core.Restore;

namespace FlowDesk.App.Services;

/// <summary>Shared wording for restore reports, so Skipped reads as informational rather than an error.</summary>
public static class RestoreReportText
{
    public static (string Message, bool HasFailures) Summary(string workspaceName, RestoreReport report)
    {
        var failures = report.Items.Where(i => i.Outcome is not (ItemOutcome.Reused or ItemOutcome.Launched or ItemOutcome.Skipped)).ToList();
        var skipped = report.Items.Where(i => i.Outcome == ItemOutcome.Skipped).ToList();

        string message = $"{workspaceName}: {report.Placed} of {report.Items.Count} windows in place.";
        if (failures.Count > 0)
            message += " " + string.Join(" ", failures.Select(i => $"{i.Name}: {i.Detail ?? Outcome(i.Outcome)}"));
        if (skipped.Count > 0)
            message += " Skipped: " + string.Join(" ", skipped.Select(i => $"{i.Name}: {i.Detail ?? "left as it was"}"));
        return (message, failures.Count > 0);
    }

    public static string Outcome(ItemOutcome outcome) => outcome switch
    {
        ItemOutcome.Reused => "Reused open window",
        ItemOutcome.Launched => "Opened",
        ItemOutcome.LaunchedNoWindow => "Opened, window not found",
        ItemOutcome.LaunchFailed => "Could not open",
        ItemOutcome.PlaceDenied => "Could not move",
        ItemOutcome.Skipped => "Skipped",
        _ => "Pending",
    };

    public static string Detail(ItemResult item) => item.Outcome switch
    {
        ItemOutcome.Reused or ItemOutcome.Launched => item.Detail ?? "Window is in place.",
        ItemOutcome.Skipped => item.Detail ?? "Left as it was.",
        ItemOutcome.LaunchedNoWindow => item.Detail ?? "The window was not found in time.",
        ItemOutcome.PlaceDenied => item.Detail ?? "Windows refused to move it.",
        _ => item.Detail ?? "This item could not be restored.",
    };
}
