using FlowDesk.App.Services;

namespace FlowDesk.App;

public sealed partial class MainWindow
{
    private UpdateCoordinator? updater;
    private VerifiedInstaller? updateInstaller;
    private bool updateLaunched;
    private void InitializeUpdater()
    {
        updater = new(new UpdateTransport(AppVersion.Display), AppVersion.Display, () => services.Settings,
            () => services.Changed(true), services.LogContextFailure, HandoffUpdateAsync);
        App.ViewModels.Settings.UpdatePreferenceChanged += updater.PreferenceChanged;
        // Import can replace the whole settings instance; enforce OFF for that path too.
        services.SettingsReplaced += updater.PreferenceChanged;
        InitializeUpdateUi(updater);
    }
    private async Task<bool> HandoffUpdateAsync(VerifiedInstaller installer)
    {
        if (closing || initializing || endingSession || App.ViewModels.IsBusy || App.ViewModels.Advanced.HasActiveInstall)
            return false;
        updateInstaller = installer;
        updateLaunched = false;
        exitRequested = true;
        try { await CloseRequestedAsync(); return updateLaunched; }
        finally { updateInstaller = null; if (!updateLaunched) exitRequested = false; }
    }
}
