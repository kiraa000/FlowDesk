using Microsoft.UI.Xaml.Controls;

namespace FlowDesk.App.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        InitializeComponent();
        DataContext = App.ViewModels.Settings;
    }
}
