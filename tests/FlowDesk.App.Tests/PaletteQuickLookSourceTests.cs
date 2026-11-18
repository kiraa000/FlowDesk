namespace FlowDesk.App.Tests;

public sealed class PaletteQuickLookSourceTests
{
    private static string Source(string folder, string file) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "AppSources", folder, file));
    [Fact] public void PreviewUsesExistingServiceKeepsPaletteOpenAndNeverReadsExplorerForExplicitPath()
    {
        string files = Source("Services", "PaletteFileSource.cs");
        Assert.Contains("new(\"Quick Look\", () => preview(path)", files);
        Assert.Contains("\"Ctrl+Y\", KeepsPaletteOpen: true", files);
        string service = Source("Services", "CommandPaletteService.cs");
        Assert.Contains("services.QuickLook.ShowPath(path, window.Handle)", service);
        Assert.Contains("Turn on Quick Look in Settings", service);
        Assert.Contains("if (pathSession.Active) return", Source("Services", "QuickLookService.cs"));
        string window = Source("Views", "CommandPaletteWindow.xaml.cs");
        Assert.Contains("VirtualKey.Escape && ClosePreview?.Invoke() == true", window);
        Assert.Contains("VirtualKey.Y", window);
    }
}
