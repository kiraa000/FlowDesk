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
    public void Sort_key_puts_current_first_then_desktop_number_and_unknown_last()
    {
        Assert.Equal((0, 5), DesktopLabels.SortKey(W(5, true)));
        Assert.Equal((1, 1), DesktopLabels.SortKey(W(1, false)));
        Assert.Equal((1, int.MaxValue), DesktopLabels.SortKey(W(null, null)));
    }
}
