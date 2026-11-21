using System.Xml.Linq;

namespace FlowDesk.App.Tests;

// Structural regression guards only; the native tray renderer still needs the supervisor's live run.
// H.NotifyIcon 2.4.1 builds the Win32 popup from the live flyout on every opening and ignores
// Visibility, so these checks keep the declared items and the code that syncs them in place.
public sealed class Task031WiringTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static string Source(params string[] path) => File.ReadAllText(Path.Combine(
        new[] { AppContext.BaseDirectory, "AppSources" }.Concat(path).ToArray()));

    [Fact]
    public void TrayMenuKeepsWorkspaceSubmenuAndStartBelowPause()
    {
        var document = XDocument.Parse(Source("MainWindow.xaml"));
        // The tray control uses xmlns:tray="using:H.NotifyIcon", so match by local name.
        var flyout = document.Descendants().Single(e => e.Name.LocalName == "TaskbarIcon")
            .Descendants().Single(e => e.Name.LocalName == "MenuFlyout");
        var names = flyout.Elements().Select(e => (string?)e.Attribute(X + "Name"))
            .Where(name => name is not null).ToArray();
        Assert.True(Array.IndexOf(names, "TrayPalette") < Array.IndexOf(names, "TrayWorkspaces"));
        Assert.True(Array.IndexOf(names, "TrayWorkspaces") < Array.IndexOf(names, "TrayPause"));
        Assert.True(Array.IndexOf(names, "TrayPause") < Array.IndexOf(names, "TrayStart"));

        var workspaces = flyout.Elements().Single(e => e.Name.LocalName == "MenuFlyoutSubItem");
        Assert.Equal("Workspaces", (string?)workspaces.Attribute("AutomationProperties.Name"));
        var start = flyout.Elements().Single(e =>
            e.Name.LocalName == "MenuFlyoutItem" && (string?)e.Attribute(X + "Name") == "TrayStart");
        Assert.StartsWith("Start focus", (string?)start.Attribute("Text"));
        Assert.False(string.IsNullOrWhiteSpace((string?)start.Attribute("AutomationProperties.Name")));
    }

    [Fact]
    public void TrayFocusItemsAreSyncedFromTheViewModelInsteadOfVisibility()
    {
        var window = Source("MainWindow.xaml.cs");
        Assert.Contains("TrayStart.Command = App.ViewModels.Focus.StartCommand;", window);
        Assert.Contains("App.ViewModels.Focus.PropertyChanged += (_, _) => UpdateTrayFocus();", window);

        var start = window.IndexOf("private void UpdateTrayFocus()", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var end = window.IndexOf("private async Task ExitFromTrayAsync", start, StringComparison.Ordinal);
        Assert.True(end > start);
        var update = window[start..end];
        Assert.Contains("UxDecisions.TrayFocusStartText(vm.CanStart, vm.Duration, vm.StrictnessIndex)", update);
        Assert.Contains("TrayStart.IsEnabled = ready && vm.CanStart;", update);
        Assert.Contains("TrayPause.IsEnabled = ready && vm.CanPause;", update);
        Assert.Contains("TrayMenu.Items.Remove(TrayPause);", update);
        Assert.Contains("TrayMenu.Items.Remove(TrayStart);", update);
        Assert.Contains("if (vm.CanPause) TrayMenu.Items.Insert", update);
        Assert.Contains("if (!vm.IsRunning) TrayMenu.Items.Insert(index, TrayStart);", update);
        Assert.Contains("DispatcherQueue.HasThreadAccess", update);

        var tickStart = window.IndexOf("private async Task UpdateFocusAsync()", StringComparison.Ordinal);
        Assert.True(tickStart >= 0 && tickStart < start);
        var tick = window[tickStart..start];
        Assert.Contains("UpdateTrayFocus();", tick);
        Assert.Contains("finally { initializing = false; UpdateTrayFocus();", window);
    }

    [Fact]
    public void WorkspaceSubmenuIsRebuiltFromLiveState()
    {
        var tray = Source("MainWindow.TrayWorkspaces.cs");
        Assert.Contains("App.ViewModels.WorkspacesCollection.CollectionChanged +=", tray);
        Assert.Contains("services.StateChanged += RefreshTrayWorkspaces;", tray);
        Assert.Contains("TrayWorkspaces.Items.Clear();", tray);
        Assert.Contains("UxDecisions.TrayWorkspaceName(workspace.Name)", tray);
        // The Win32 popup is a snapshot, so each item runs the card command through a click-time
        // guard instead of binding workspace.OpenCommand directly.
        Assert.Contains("workspace.OpenCommand.Execute(null)", tray);
        foreach (var token in new[]
        {
            "!initializing", "!closing", "!endingSession", "!exitRequested", "!allowClose",
            "!App.ViewModels.IsBusy", "App.ViewModels.WorkspacesCollection.Contains(workspace)"
        })
            Assert.Contains(token, tray);
        Assert.Contains("No workspaces yet", tray);

        var window = Source("MainWindow.xaml.cs");
        var busy = window.IndexOf("nameof(AppViewModels.IsBusy)", StringComparison.Ordinal);
        Assert.True(busy >= 0);
        var handler = window[busy..Math.Min(window.Length, busy + 400)];
        Assert.Contains("RefreshTrayWorkspaces();", handler);
        Assert.Contains("finally { initializing = false; UpdateTrayFocus(); RefreshTrayWorkspaces(); }", window);
    }

    [Fact]
    public void CanceledOrFailedCloseRefreshesWorkspaceCommands()
    {
        var window = Source("MainWindow.xaml.cs");
        var closing = window.IndexOf("closing = true;", StringComparison.Ordinal);
        Assert.True(closing >= 0);
        // Stale IsEnabled on the open popup snapshot must be corrected as soon as the close starts.
        Assert.Contains("RefreshTrayWorkspaces();", window[closing..Math.Min(window.Length, closing + 80)]);

        var cancel = window.IndexOf("if (!await advancedDialogs.WaitBeforeExitAsync())", StringComparison.Ordinal);
        Assert.True(cancel >= 0);
        var cancelBody = window[cancel..window.IndexOf(
            "await App.ViewModels.Advanced.WaitForInstallAsync();", cancel, StringComparison.Ordinal)];
        Assert.Contains("closing = false; exitRequested = false; RefreshTrayWorkspaces();", cancelBody);

        var declined = window.IndexOf("if (!exitRequested && !await App.ViewModels.Focus.EndSessionAsync", StringComparison.Ordinal);
        Assert.True(declined >= 0);
        var declinedBody = window[declined..window.IndexOf("catch (Exception)", declined, StringComparison.Ordinal)];
        Assert.Contains("closing = false; RefreshTrayWorkspaces();", declinedBody);

        var failed = window.IndexOf("catch (Exception)", declined, StringComparison.Ordinal);
        Assert.True(failed > declined);
        var failedBody = window[failed..window.IndexOf(
            "// Do not let a new focus session or autosave tick start", failed, StringComparison.Ordinal)];
        // The interactive catch path leaves the window open, so it must re-enable the menu too.
        Assert.Contains("closing = false; exitRequested = false; PageFrame.IsEnabled = true; Navigation.IsEnabled = true;", failedBody);
        Assert.Contains("RefreshTrayWorkspaces();", failedBody);

        var resume = window.IndexOf("private void ResumeAfterFailedClose()", StringComparison.Ordinal);
        Assert.True(resume > failed);

        var resumeBody = window[resume..window.IndexOf("// DisplayArea.FindAll()", resume, StringComparison.Ordinal)];
        Assert.Contains("allowClose = false; closing = false; exitRequested = false;", resumeBody);
        Assert.Contains("RefreshTrayWorkspaces();", resumeBody);
    }

    [Fact]
    public void FocusPageShowsEndTimeFromTheSessionState()
    {
        var page = Source("Views", "FocusPage.xaml");
        Assert.True(page.IndexOf("RemainingText", StringComparison.Ordinal) <
            page.IndexOf("EndTimeText", StringComparison.Ordinal));
        Assert.Contains("Text=\"{Binding EndTimeText}\"", page);
        Assert.Contains("CaptionTextBlockStyle", page);
        var viewModel = Source("ViewModels", "FocusViewModel.cs");
        Assert.Contains("public partial string EndTimeText", viewModel);
        Assert.Contains("UxDecisions.FocusEndTime(sample.state?.EndsAt.ToLocalTime(), sample.state?.Paused == true,", viewModel);
    }

    [Fact]
    public void CalmLastCheckedUsesFriendlyWordingOnEveryVisit()
    {
        var viewModel = Source("ViewModels", "CalmViewModel.cs");
        Assert.Contains("UxDecisions.CalmLastChecked(now, now,", viewModel);
        Assert.Contains("lastCheckedAt is { } checkedAt", viewModel);
        Assert.Contains("UxDecisions.CalmLastChecked(checkedAt.ToLocalTime(), DateTimeOffset.Now,", viewModel);
        Assert.DoesNotContain("\"Last checked \" + DateTimeOffset.Now.ToString(\"g\")", viewModel);
    }

    [Fact]
    public void GameModeHeadingUsesTheSameStyleAsShortcutGroupHeadings()
    {
        var document = XDocument.Parse(Source("Views", "HotkeysPage.xaml"));
        var game = document.Descendants(Xaml + "TextBlock")
            .Single(e => (string?)e.Attribute("Text") == "Game mode");
        Assert.Equal("{StaticResource SubtitleTextBlockStyle}", (string?)game.Attribute("Style"));
        Assert.Equal("0,0,0,8", (string?)game.Attribute("Margin"));
        var group = document.Descendants(Xaml + "TextBlock")
            .Single(e => (string?)e.Attribute("Text") == "{Binding Title}");
        Assert.Equal((string?)group.Attribute("Style"), (string?)game.Attribute("Style"));
    }
}
