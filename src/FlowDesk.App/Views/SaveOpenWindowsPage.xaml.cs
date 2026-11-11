using Microsoft.UI.Xaml.Controls;

namespace FlowDesk.App.Views;

public sealed partial class SaveOpenWindowsPage : Page
{
    public SaveOpenWindowsPage()
    {
        InitializeComponent();
        DataContext = App.ViewModels.Capture;
    }
}
