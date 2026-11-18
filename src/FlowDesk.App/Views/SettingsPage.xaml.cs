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
    private async void AddCustomCommand_Click(object sender, RoutedEventArgs e) => await EditCustomAsync(null);
    private async void EditCustomCommand_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: FlowDesk.Core.Palette.CustomCommandDefinition command }) await EditCustomAsync(command);
    }
    private async Task EditCustomAsync(FlowDesk.Core.Palette.CustomCommandDefinition? command)
    {
        var vm = App.ViewModels.Settings;
        var editor = new CustomCommandEditor(command, vm.CustomCommands.ToArray());
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = command is null ? "Add custom command" : "Edit custom command",
            Content = editor, PrimaryButtonText = "Save", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (!editor.Validate()) { args.Cancel = true; return; }
            if (!vm.SaveCustomCommand(editor.Value)) { editor.ShowError(vm.CustomCommandsError); args.Cancel = true; }
        };
        await dialog.ShowAsync();
    }
    private async void DeleteCustomCommand_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: FlowDesk.Core.Palette.CustomCommandDefinition command }) return;
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Delete custom command?", Content = command.Name,
            PrimaryButtonText = "Delete", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary) App.ViewModels.Settings.DeleteCustomCommand(command.Id);
    }
    private async void TestCustomCommand_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: FlowDesk.Core.Palette.CustomCommandDefinition command }) return;
        var query = new TextBox { Header = "Query (optional)", PlaceholderText = "Text for {query}" };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(query, "Test command query");
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Test " + command.Name, Content = query,
            PrimaryButtonText = "Run", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary) await App.ViewModels.Settings.TestCustomCommandAsync(command, query.Text);
    }
}
