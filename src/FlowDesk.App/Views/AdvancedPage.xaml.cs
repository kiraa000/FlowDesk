using Microsoft.UI.Xaml.Controls;

namespace FlowDesk.App.Views;

public sealed partial class AdvancedPage : Page
{
    public AdvancedPage()
    {
        InitializeComponent();
        DataContext = App.ViewModels.Advanced;
        Loaded += async (_, _) => await App.ViewModels.Advanced.RefreshAsync();
    }
}
