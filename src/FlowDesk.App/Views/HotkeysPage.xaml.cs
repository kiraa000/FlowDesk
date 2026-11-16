using Microsoft.UI.Xaml.Controls;

namespace FlowDesk.App.Views;

public sealed partial class HotkeysPage : Page
{
    public HotkeysPage()
    {
        InitializeComponent();
        DataContext = App.ViewModels.Hotkeys;
        Loaded += (_, _) =>
        {
            var command = App.ViewModels.Hotkeys.RefreshCommand;
            if (command.CanExecute(null)) command.Execute(null);
        };
    }
}
