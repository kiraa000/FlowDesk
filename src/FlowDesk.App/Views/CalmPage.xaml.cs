using Microsoft.UI.Xaml.Controls;

namespace FlowDesk.App.Views;

public sealed partial class CalmPage : Page
{
    public CalmPage()
    {
        InitializeComponent();
        DataContext = App.ViewModels.Calm;
        // Navigation never causes a scan. Only first feature start and the explicit button do.
    }
}
