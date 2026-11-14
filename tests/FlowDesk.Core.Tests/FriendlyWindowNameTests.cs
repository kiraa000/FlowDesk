using System.Diagnostics;
using FlowDesk.Core.Capture;
using FlowDesk.Core.Model;
using FlowDesk.Core.Windows;

namespace FlowDesk.Core.Tests;

public sealed class FriendlyWindowNameTests
{
    private static WindowInfo Window(string path, string title, string? aumid = null) => new()
    {
        Handle = 1, ProcessId = 2, ExePath = path, Title = title, Aumid = aumid, ClassName = "Test",
        NormalRect = new(0, 0, 800, 600), Show = WindowShow.Normal
    };

    [Fact]
    public void Shared_capture_and_layout_name_uses_friendly_packaged_app_name_not_process()
    {
        var window = Window("calculatorapp.exe", "Calculation — Calculator", "Microsoft.Calculator!App");
        Assert.Equal("Calculator", WorkspaceCapture.DisplayName(window));
        Assert.Equal("Calculation — Calculator", window.Title); // Title remains the secondary line.
    }

    [Fact]
    public void Shared_capture_and_layout_name_reads_executable_description()
    {
        string path = typeof(WorkspaceCapture).Assembly.Location;
        var metadata = FileVersionInfo.GetVersionInfo(path);
        Assert.False(string.IsNullOrWhiteSpace(metadata.FileDescription));
        Assert.Equal(metadata.FileDescription.Trim(), WorkspaceCapture.DisplayName(Window(path, "Document title")));
    }

    [Fact]
    public void Shared_name_keeps_capture_fallbacks_when_metadata_is_unavailable()
    {
        Assert.Equal("Document title", WorkspaceCapture.DisplayName(Window("", "Document title")));
        Assert.Equal("missing-flowdesk-fixture", WorkspaceCapture.DisplayName(
            Window("missing-flowdesk-fixture.exe", "Document title")));
    }
}
