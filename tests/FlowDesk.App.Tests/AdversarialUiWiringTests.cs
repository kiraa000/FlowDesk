namespace FlowDesk.App.Tests;

// Structural checks only: WinUI/notification behavior still requires the supervisor's live run.
public sealed class AdversarialUiWiringTests
{
    private static string Source(params string[] path) => File.ReadAllText(Path.Combine(
        new[] { AppContext.BaseDirectory, "AppSources" }.Concat(path).ToArray()));

    [Fact]
    public void Hiding_tour_clears_and_completes_pending_close()
    {
        var source = Source("MainWindow.Tour.cs");
        var stop = source[source.IndexOf("private void StopTour()", StringComparison.Ordinal)..];
        Assert.Contains("HideTour();", stop);
        Assert.Contains("tourTipClosed = null;", stop);
        Assert.Contains("completion?.TrySetResult(true);", stop);
        Assert.Contains("if (TourTip.IsOpen) return;", source);
    }

    [Fact]
    public void Notification_failure_preserves_reason_and_stale_activation_reports_unavailable()
    {
        var source = Source("Services", "FocusNotifications.cs");
        Assert.Contains("if (!Show(message, actions)) error(message);", source);
        Assert.Contains("else error(\"That allowance is no longer available.\");", source);
        var vm = Source("ViewModels", "FocusViewModel.cs");
        Assert.Contains("owner.Services.Focus.AppBlocked +=", vm);
        Assert.Contains("generation != allowanceGeneration", vm);
        Assert.Contains("owner.Services.Focus.AllowTemporarily(window)", vm);
        Assert.Contains("sample.state.Strictness != FocusStrictness.Normal) ClearLastBlocked()", vm);
        var markup = Source("Views", "FocusPage.xaml");
        Assert.Contains("Command=\"{Binding AllowLastBlockedCommand}\"", markup);
        Assert.Contains("Visibility=\"{Binding CanAllowLastBlocked,", markup);
    }

    [Fact]
    public void Dispatcher_rejection_is_not_silently_ignored_and_autosave_uses_tested_boundary()
    {
        var source = Source("Services", "UserInteraction.cs");
        Assert.Contains("public bool TryEnqueue(Action action) => window.DispatcherQueue.TryEnqueue", source);
        source = Source("Services", "AppAutoSaveHost.cs");
        Assert.Contains("UiDispatchTask.Run(_ui.TryEnqueue, func)", source);
        Assert.Contains("UiDispatchTask.RunAsync(_ui.TryEnqueue, func)", source);
        Assert.Contains("applied.DesktopIndex ??= item.Window?.DesktopIndex", source);
        Assert.Contains("applied.DesktopId ??= item.Window?.DesktopId", source);
    }

    [Fact]
    public void Picker_and_confirm_are_inside_exception_boundary_and_keyboard_failure_has_visible_status()
    {
        var source = Source("ViewModels", "PageViewModels.cs");
        var choose = source[source.IndexOf("private async Task ChooseShortcutsDllAsync()", StringComparison.Ordinal)..];
        Assert.True(choose.IndexOf("try", StringComparison.Ordinal) < choose.IndexOf("await owner.Ui.PickFileAsync", StringComparison.Ordinal));
        Assert.True(choose.IndexOf("try", StringComparison.Ordinal) < choose.IndexOf("await owner.Ui.ConfirmAsync", StringComparison.Ordinal));
        Assert.Contains("hotkeys.KeyboardHookWarning", source);
        Assert.Contains("owner.Services.Hotkeys.KeyboardHookWarning", source);
    }

    [Theory]
    [InlineData("AddCustomCommand_Click")]
    [InlineData("EditCustomCommand_Click")]
    [InlineData("DeleteCustomCommand_Click")]
    [InlineData("TestCustomCommand_Click")]
    [InlineData("ExportTransfer_Click")]
    [InlineData("ImportTransfer_Click")]
    public void Settings_dialog_handlers_guard_and_release(string method)
    {
        var source = Source("Views", "SettingsPage.xaml.cs");
        var start = source.IndexOf("private async void " + method, StringComparison.Ordinal);
        var end = source.IndexOf("private ", start + 1, StringComparison.Ordinal);
        var body = end < 0 ? source[start..] : source[start..end];
        Assert.Contains("if (dialogOpen", body);
        Assert.Contains("dialogOpen = true;", body);
        Assert.Contains("catch (Exception", body);
        Assert.Contains("finally { dialogOpen = false;", body);
    }
}
