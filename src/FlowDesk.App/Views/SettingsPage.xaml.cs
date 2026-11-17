using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;

namespace FlowDesk.App.Views;

public sealed partial class SettingsPage : Page
{
    private bool ready, changing;
    public SettingsPage()
    {
        InitializeComponent();
        DataContext = App.ViewModels.Settings;
        // Re-read registry-backed state (Start with Windows) every time Settings is shown.
        App.ViewModels.Settings.Load();
        AdvancedModeSwitch.IsOn = App.ViewModels.Advanced.Enabled;
        ready = true;
        Loaded += (_, _) => { if (App.ViewModels.FocusAdvancedSettingRequested) FocusAdvancedSetting(); };
    }
    public void FocusAdvancedSetting()
    {
        if (!IsLoaded) return; // Loaded consumes the request after layout.
        App.ViewModels.FocusAdvancedSettingRequested = false;
        DispatcherQueue.TryEnqueue(() =>
        {
            AdvancedModeCard.StartBringIntoView();
            AdvancedModeSwitch.Focus(FocusState.Programmatic);
        });
    }
    private async void AdvancedMode_Toggled(object sender, RoutedEventArgs e)
    {
        if (!ready || changing) return;
        var focusState = AdvancedModeSwitch.FocusState == FocusState.Keyboard ? FocusState.Keyboard : FocusState.Programmatic;
        changing = true;
        bool requested = AdvancedModeSwitch.IsOn;
        // Never publish the optimistic switch value to settings/navigation before agreement.
        AdvancedModeSwitch.IsOn = App.ViewModels.Advanced.Enabled;
        AdvancedModeSwitch.IsEnabled = false;
        try { await App.ViewModels.Advanced.RequestEnabledAsync(requested); }
        catch (Exception ex) { App.ViewModels.ShowStatus("Advanced mode could not be changed: " + ex.Message, ViewModels.StatusKind.Warning); }
        finally
        {
            AdvancedModeSwitch.IsOn = App.ViewModels.Advanced.Enabled;
            AdvancedModeSwitch.IsEnabled = true; changing = false;
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                if (IsLoaded && AdvancedModeSwitch.IsEnabled) AdvancedModeSwitch.Focus(focusState);
            });
        }
    }
}
