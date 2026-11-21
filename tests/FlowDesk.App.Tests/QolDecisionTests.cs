using System.Globalization;
using FlowDesk.App.Services;

namespace FlowDesk.App.Tests;

public sealed class QolDecisionTests
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");
    private static readonly DateTimeOffset Evening = new(2026, 11, 20, 23, 45, 0, TimeSpan.Zero);

    [Fact]
    public void EndTimeUsesShortTimeAndTracksExtensionPauseAndEnd()
    {
        Assert.Equal("Ends at 11:45 PM", UxDecisions.FocusEndTime(Evening, false, English));
        Assert.Equal("Ends at 11:50 PM", UxDecisions.FocusEndTime(Evening.AddMinutes(5), false, English));
        Assert.Equal("Paused", UxDecisions.FocusEndTime(Evening, true, English));
        Assert.Equal("", UxDecisions.FocusEndTime(null, false, English));
        Assert.Equal("Ends at 23:45", UxDecisions.FocusEndTime(Evening, false, CultureInfo.GetCultureInfo("en-GB")));
    }

    [Fact]
    public void CalmUsesCalendarDaysNotElapsedHoursAcrossMidnightAndYearBoundary()
    {
        var now = new DateTimeOffset(2027, 1, 1, 0, 5, 0, TimeSpan.Zero);
        Assert.Equal("Last checked today at 12:05 AM", UxDecisions.CalmLastChecked(now, now, English));
        Assert.Equal("Last checked yesterday at 11:55 PM", UxDecisions.CalmLastChecked(now.AddMinutes(-10), now, English));
        var earlier = now.AddDays(-2);
        Assert.Equal("Last checked on " + earlier.ToString(English.DateTimeFormat.MonthDayPattern, English) + " at 12:05 AM",
            UxDecisions.CalmLastChecked(earlier, now, English));
        Assert.Equal("Last checked on " + now.AddDays(1).ToString(English.DateTimeFormat.MonthDayPattern, English) + " at 12:05 AM",
            UxDecisions.CalmLastChecked(now.AddDays(1), now, English));
    }

    [Fact]
    public void CalmHonorsCultureMonthDayAndTimePatterns()
    {
        var culture = CultureInfo.GetCultureInfo("de-DE");
        Assert.Equal("Last checked today at 23:45", UxDecisions.CalmLastChecked(Evening, Evening, culture));
        var older = Evening.AddDays(-2);
        Assert.Equal($"Last checked on {older.ToString(culture.DateTimeFormat.MonthDayPattern, culture)} at 23:45",
            UxDecisions.CalmLastChecked(older, Evening, culture));
    }

    [Theory]
    [InlineData(true, 25, 1, "Start focus (25 min)")]
    [InlineData(true, 50, 2, "Start focus (50 min, Strict)")]
    [InlineData(true, 90, 0, "Start focus (90 min, Gentle)")]
    [InlineData(false, 50, 2, "Start focus — add a blocked app first")]
    public void TrayStartMatchesPageChoices(bool canStart, int minutes, int strictness, string expected) =>
        Assert.Equal(expected, UxDecisions.TrayFocusStartText(canStart, minutes, strictness));

    [Fact]
    public void WorkspaceLabelsKeepShortNamesAndTruncateWithoutSplittingTextElements()
    {
        Assert.Equal("", UxDecisions.TrayWorkspaceName(""));
        Assert.Equal(new string('a', 40), UxDecisions.TrayWorkspaceName(new string('a', 40)));
        Assert.Equal(new string('a', 39) + "…", UxDecisions.TrayWorkspaceName(new string('a', 41)));
        string emoji = string.Concat(Enumerable.Repeat("🧑‍💻", 41));
        Assert.Equal(string.Concat(Enumerable.Repeat("🧑‍💻", 39)) + "…", UxDecisions.TrayWorkspaceName(emoji));
        // Combining sequences are one text element each, so a 40-element name must not be cut.
        string accented = string.Concat(Enumerable.Repeat("e\u0301", 40));
        Assert.Equal(accented, UxDecisions.TrayWorkspaceName(accented));
        Assert.Equal(string.Concat(Enumerable.Repeat("e\u0301", 39)) + "…",
            UxDecisions.TrayWorkspaceName(accented + "e\u0301"));
    }
}
