using Microsoft.UI.Xaml.Controls;

namespace FlowDesk.App.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        InitializeComponent();
        DataContext = App.ViewModels.Settings;
        // Re-read registry-backed state (Start with Windows) every time Settings is shown.
        App.ViewModels.Settings.Load();
    }
}
