namespace FlowDesk.App.Tests;

// Structural guard for the WinUI glue; executable decision tests live in PaletteSafetyTests.
public sealed class PaletteExecutionSourceTests
{
    [Fact] public void ConfirmationUpdatesBothVisibleSurfacesAndForegroundRequestsRequireActualOwnership()
    {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "AppSources", "Views", "CommandPaletteWindow.xaml.cs"));
        Assert.Contains("confirmationRow.ConfirmationPrompt = ConfirmationText.Text", source);
        Assert.Contains("ConfirmationText.Visibility = Visibility.Visible", source);
        Assert.Contains("PaletteFocusRules.ShouldHideOnDeactivation(IsOpen, opening, activationPending, foregroundAcquired)", source);
        Assert.DoesNotContain("SetForegroundWindow(hwnd) ||", source);
        Assert.Contains("RequestForegroundWithInputAttachment()", source);
        var doc = PaletteMarkupFixture.Load();
        Assert.Contains(doc.Descendants(PaletteMarkupFixture.Xaml + "TextBlock"), e =>
            (string?)e.Attribute("Text") == "{Binding ConfirmationPrompt}" &&
            (string?)e.Attribute("Visibility") == "{Binding ConfirmationTitleVisibility}");
    }
    [Fact]
    public void EveryUiInvocationUsesTheSharedExecutorAndPanelRowsCarryNoExecutableDelegate()
    {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "AppSources", "Views", "CommandPaletteWindow.xaml.cs"));
        Assert.Contains("executor.InvokeAsync(row.Match.Item, index, input", source);
        Assert.DoesNotContain("await action.Run()", source);
        Assert.Contains("if (panel is { } p) _ = RunAsync(p.Target, Results.SelectedIndex, input)", source);
        Assert.Contains("if (panel is { } p) _ = RunAsync(p.Target, Results.Items.IndexOf(row), PaletteInvocation.Pointer)", source);
        Assert.Contains("() => Task.CompletedTask, NamedActions: []", source);
        Assert.Contains("if (executor.IsPending) { refreshAfterConfirmation = true; return; }", source);
    }
}
