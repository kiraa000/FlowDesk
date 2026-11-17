using Microsoft.UI.Xaml.Controls;

namespace FlowDesk.App.Views;

public sealed partial class CalmPage : Page
{
    public CalmPage()
    {
        InitializeComponent();
        DataContext = App.ViewModels.Calm;
        Loaded += async (_, _) => await App.ViewModels.Calm.EnsureScanForVisitAsync();
        // First visit each run reads current state; subsequent visits reuse it. No writes or offer here.
    }
}
