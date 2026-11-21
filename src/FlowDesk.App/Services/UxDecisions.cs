using System.Globalization;

namespace FlowDesk.App.Services;

public enum CloseAction { Exit, Hide }

/// <summary>Pure decisions shared by the live UI and headless regression tests.</summary>
public static class UxDecisions
{
    public const bool InitialWindowSelection = false;
    public static string NextWorkspaceName(IEnumerable<string> names)
    {
        var used = names.Select(n => n.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        int number = 1;
        while (used.Contains($"Workspace {number}")) number++;
        return $"Workspace {number}";
    }
    public static bool DuplicateWorkspaceName(string name, IEnumerable<string> others) =>
        !string.IsNullOrWhiteSpace(name) && others.Any(n => string.Equals(n.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase));
    public static CloseAction Close(bool explicitExit, bool endingSession, bool loaded, bool trayReady) =>
        explicitExit || endingSession || !loaded ? CloseAction.Exit
        : trayReady ? CloseAction.Hide : CloseAction.Exit;

    // Delivery is synchronous, before hiding. A successful platform handoff does not promise a banner
    // (Windows can suppress it with Do not disturb). On failure the caller keeps the window open and shows
    // the explanation inline; that counts as shown (user 2026-11-14: "explain once, then hide"), so the next
    // close hides to the tray instead of keeping the window open forever when notifications are off.
    public static bool CanHideWithTrayNotice(AppSettings settings, Func<bool> deliver)
    {
        if (settings.TrayCloseNoticeShown) return true;
        bool delivered = deliver();
        settings.TrayCloseNoticeShown = true;
        return delivered;
    }

    public static bool CanSaveWindows(IEnumerable<(bool Selected, bool CanSelect)> windows) =>
        windows.Any(w => w.Selected && w.CanSelect);

    public static bool SelectWindow(bool selectAll, bool canSelect) => selectAll && canSelect;
    public static bool CanStartFocus(bool running, bool blockAll, int blockedCount) =>
        !running && (blockAll || blockedCount > 0);
    public static string TrayWorkspaceName(string name)
    {
        // Count text elements, not UTF-16 halves: workspace names may contain emoji.
        var starts = StringInfo.ParseCombiningCharacters(name);
        return starts.Length <= 40 ? name : name[..starts[39]] + "…";
    }
    public static string TrayFocusStartText(bool canStart, int minutes, int strictnessIndex) => !canStart
        ? "Start focus — add a blocked app first"
        : $"Start focus ({minutes} min{(strictnessIndex == 0 ? ", Gentle" : strictnessIndex == 2 ? ", Strict" : "")})";

    public static string FocusEndTime(DateTimeOffset? endsAt, bool paused, CultureInfo culture) =>
        endsAt is null ? "" : paused ? "Paused" : "Ends at " + endsAt.Value.ToString("t", culture);

    // Inputs are local wall-clock values supplied by the caller (tests inject both now and culture).
    public static string CalmLastChecked(DateTimeOffset checkedAt, DateTimeOffset now, CultureInfo culture)
    {
        var day = checkedAt.Date;
        string when = day == now.Date ? "today" : day == now.Date.AddDays(-1) ? "yesterday"
            : "on " + checkedAt.ToString(culture.DateTimeFormat.MonthDayPattern, culture);
        return $"Last checked {when} at {checkedAt.ToString("t", culture)}";
    }
    /// <summary>Calm offer: the first check offers every finding; later checks offer only findings that were not
    /// there at the last offer (e.g. Windows turned a suggestion back on). An install that never recorded the set
    /// takes today's findings as its baseline without an offer.</summary>
    public static (int Offer, bool New, List<string> Store) CalmOffer(bool firstDone, IReadOnlyCollection<string>? offered,
        IReadOnlyCollection<string> current)
    {
        var store = current.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        if (!firstDone) return (store.Count, false, store);
        if (offered is null) return (0, false, store);
        var known = offered.ToHashSet(StringComparer.Ordinal);
        return (store.Count(id => !known.Contains(id)), true, store);
    }
    public static bool ConfirmFocusEnd(bool hasSession, bool strict, bool exiting) =>
        hasSession && (strict || exiting);
}
