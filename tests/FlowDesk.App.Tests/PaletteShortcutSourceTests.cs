namespace FlowDesk.App.Tests;

public sealed class PaletteShortcutSourceTests
{
    [Fact]
    public void AppDoesNotHardCodeThePaletteShortcut()
    {
        string[] sources = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "AppSources"), "*", SearchOption.AllDirectories);
        Assert.NotEmpty(sources);
        foreach (string source in sources)
        {
            string text = File.ReadAllText(source);
            Assert.DoesNotContain("Ctrl+Alt+Space", text, StringComparison.OrdinalIgnoreCase);
            // Even the current default belongs only in the Core catalog: help/status text must track it.
            Assert.DoesNotContain("Win+Alt+Space", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Win+Alt+V", text, StringComparison.OrdinalIgnoreCase);
        }
    }
}
