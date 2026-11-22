using CommunityToolkit.Mvvm.Input;
using FlowDesk.App.Services;
using FlowDesk.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace FlowDesk.App;

// The update notice, download progress and tray entry. Checking, downloading and installing stay in the coordinator.
public sealed partial class MainWindow
{
    private UpdateCoordinator? updateCoordinator;

    private void InitializeUpdateUi(UpdateCoordinator updater)
    {
        if (updateCoordinator is not null) return;
        updateCoordinator = updater;
        updater.StateChanged += OnUpdateStateChanged;
        TrayInstallUpdate.Command = new AsyncRelayCommand(InstallFromUpdateUiAsync);
        Closed += (_, _) =>
        {
            if (updateCoordinator is { } current) current.StateChanged -= OnUpdateStateChanged;
        };
        RenderUpdateState();
    }

    private void OnUpdateStateChanged()
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(RenderUpdateState);
            return;
        }
        RenderUpdateState();
    }

    private void RenderUpdateState()
    {
        if (updateCoordinator is not { } updater) return;
        var state = updater.State;
        bool pending = state.Pending is not null;
        bool busy = state.Busy;
        bool hasError = !string.IsNullOrWhiteSpace(state.Error);

        UpdateInfoBar.IsOpen = pending || busy || hasError;
        UpdateInfoBar.Severity = hasError ? InfoBarSeverity.Error : InfoBarSeverity.Informational;
        UpdateInfoBar.Message = busy ? "Downloading update\u2026"
            : hasError ? state.Error!
            : pending ? $"FlowDesk {state.Pending!.Version} is available"
            : "";

        UpdateProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        if (busy)
        {
            UpdateProgress.IsIndeterminate = state.Progress is null;
            UpdateProgress.Value = state.Progress is { } progress ? Math.Clamp(progress, 0, 100) : 0;
        }

        UpdateInstallButton.Visibility = pending && !busy ? Visibility.Visible : Visibility.Collapsed;
        UpdateInstallButton.IsEnabled = updater.CanInstall;
        UpdateNotesButton.Visibility = (pending || hasError) && updater.ReleaseNotes is not null
            ? Visibility.Visible : Visibility.Collapsed;
        UpdateLaterButton.Visibility = pending && !busy ? Visibility.Visible : Visibility.Collapsed;
        UpdateCancelButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;

        bool showTray = pending && !busy;
        if (showTray)
        {
            string text = $"Install update {state.Pending!.Version}";
            TrayInstallUpdate.Text = text;
            AutomationProperties.SetName(TrayInstallUpdate, text);
            TrayInstallUpdate.IsEnabled = updater.CanInstall;
            if (!TrayMenu.Items.Contains(TrayInstallUpdate))
            {
                int index = TrayMenu.Items.IndexOf(TrayUpdateSeparator);
                if (index >= 0) TrayMenu.Items.Insert(index, TrayInstallUpdate);
                else TrayMenu.Items.Add(TrayInstallUpdate);
            }
        }
        else
        {
            TrayMenu.Items.Remove(TrayInstallUpdate);
        }
    }

    private async Task InstallFromUpdateUiAsync()
    {
        BringForward();
        if (updateCoordinator is not { } updater) return;
        try { await updater.InstallAsync(); }
        catch (Exception) { BringForward(); }
    }

    private void UpdateInstall_Click(object sender, RoutedEventArgs e) => _ = InstallFromUpdateUiAsync();

    private void UpdateNotNow_Click(object sender, RoutedEventArgs e)
    {
        updateCoordinator?.Skip();
        RenderUpdateState();
    }

    private void UpdateCancel_Click(object sender, RoutedEventArgs e) => updateCoordinator?.CancelDownload();

    private async void UpdateReleaseNotes_Click(object sender, RoutedEventArgs e)
    {
        if (updateCoordinator?.ReleaseNotes is not { } notes) return;
        try
        {
            if (!await Windows.System.Launcher.LaunchUriAsync(notes)) ShowReleaseNotesError(notes);
        }
        catch (Exception) { ShowReleaseNotesError(notes); }
    }

    private static void ShowReleaseNotesError(Uri notes) =>
        App.ViewModels.ShowStatus("Release notes could not be opened. Try again from your browser at " + notes,
            StatusKind.Error);
}
