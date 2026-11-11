using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FlowDesk.App.Views;

public sealed partial class WorkspacesPage : Page
{
    public WorkspacesPage()
    {
        InitializeComponent();
        DataContext = App.ViewModels.Workspaces;
    }

    private void NewWorkspace_Click(SplitButton sender, SplitButtonClickEventArgs e)
    {
        if (DataContext is FlowDesk.App.ViewModels.WorkspacesViewModel vm &&
            vm.NewFromWindowsCommand.CanExecute(null))
            vm.NewFromWindowsCommand.Execute(null);
    }
}
