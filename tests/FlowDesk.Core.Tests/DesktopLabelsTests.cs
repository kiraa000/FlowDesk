using FlowDesk.App.ViewModels;
using FlowDesk.Core.Model;
using FlowDesk.Core.Windows;

namespace FlowDesk.Core.Tests;

/// <summary>Compact desktop labels shared by the save/picker UI (linked pure App source, no UI).</summary>
public class DesktopLabelsTests
{
    private static readonly Guid DesktopId = Guid.Parse("12345678-0000-0000-0000-000000000000");

    private static WindowInfo W(int? index, bool? current) => new()
    {
        Handle = 1, ProcessId = 2, ExePath = @"C:\apps\a.exe", Title = "A", ClassName = "Win",
        NormalRect = new(0, 0, 100, 100), Show = WindowShow.Normal,
        DesktopIndex = index, IsOnCurrentDesktop = current,
    };

    [Theory]
    [InlineData(1, true, "Desktop 2 (current)")]
    [InlineData(2, false, "Desktop 3")]
    [InlineData(0, null, "Desktop 1")]
    [InlineData(null, false, "Other desktop")]
    [InlineData(null, true, "Current desktop")]
    [InlineData(null, null, "")]
    public void Window_label_uses_the_number_and_marks_the_current_desktop(int? index, bool? current, string expected)
    {
        Assert.Equal(expected, DesktopLabels.For(W(index, current)));
    }

    [Fact]
    public void Saved_window_label_never_invents_a_number_from_an_identity()
    {
        Assert.Equal("Desktop 2", DesktopLabels.For(new SavedWindow { DesktopIndex = 1 }));
        Assert.Equal("Desktop (number unavailable)", DesktopLabels.For(new SavedWindow { DesktopId = DesktopId }));
        Assert.Equal("", DesktopLabels.For(new SavedWindow()));
        Assert.Equal("", DesktopLabels.For((SavedWindow?)null));
    }

    [Fact]
    public void Prefix_leads_with_the_desktop_and_leaves_unknown_text_alone()
    {
        Assert.Equal("Desktop 2 (current) · title", DesktopLabels.Prefix(W(1, true), "title"));
        Assert.Equal("title", DesktopLabels.Prefix(W(null, null), "title"));
    }

    [Fact]
    public void Unknown_index_on_current_desktop_uses_number_from_other_current_windows()
    {
        WindowInfo[] windows = [W(4, false), W(null, true), W(2, true), W(null, null)];
        int? current = DesktopLabels.CurrentDesktopIndex(windows);
        Assert.Equal(2, current);
        Assert.Equal("Desktop 3 (current)", DesktopLabels.For(windows[1], current));
        Assert.Equal(DesktopLabels.For(windows[2], current), DesktopLabels.For(windows[1], current));
        Assert.Equal("Desktop 3 (current) · title", DesktopLabels.Prefix(windows[1], "title", current));
        Assert.Equal("Desktop 5", DesktopLabels.For(windows[0], current));
        Assert.Equal("", DesktopLabels.For(windows[3], current));
    }

    [Fact]
    public void Unknown_current_number_is_not_inferred_from_another_desktop()
    {
        WindowInfo[] windows = [W(4, false), W(null, true), W(2, null)];
        Assert.Null(DesktopLabels.CurrentDesktopIndex(windows));
        Assert.Null(DesktopLabels.CurrentDesktopIndex([]));
        Assert.Equal("Current desktop", DesktopLabels.For(windows[1], DesktopLabels.CurrentDesktopIndex(windows)));
        Assert.Equal("Other desktop", DesktopLabels.For(W(null, false), 2));
    }

    [Fact]
    public void Shared_current_label_does_not_change_sort_order_or_stability()
    {
        WindowInfo[] windows = [W(4, false) with { Handle = 1 }, W(null, true) with { Handle = 2 },
            W(2, true) with { Handle = 3 }, W(null, true) with { Handle = 4 }];
        var before = windows.OrderBy(DesktopLabels.SortKey).Select(w => w.Handle).ToArray();
        int? current = DesktopLabels.CurrentDesktopIndex(windows);
        foreach (var window in windows) _ = DesktopLabels.For(window, current);
        Assert.Equal(new long[] { 3, 2, 4, 1 }, before);
        Assert.Equal(before, windows.OrderBy(DesktopLabels.SortKey).Select(w => w.Handle));
    }

    [Fact]
    public void Sort_key_puts_current_first_then_desktop_number_and_unknown_last()
    {
        Assert.Equal((0, 5), DesktopLabels.SortKey(W(5, true)));
        Assert.Equal((1, 1), DesktopLabels.SortKey(W(1, false)));
        Assert.Equal((1, int.MaxValue), DesktopLabels.SortKey(W(null, null)));
    }
}
