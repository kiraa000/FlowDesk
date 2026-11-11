using Microsoft.UI.Xaml.Controls;

namespace FlowDesk.App.Views;

public sealed partial class WorkspaceDetailsPage : Page
{
    public WorkspaceDetailsPage()
    {
        InitializeComponent();
        DataContext = App.ViewModels.Details;
    }

    private void Breadcrumbs_ItemClicked(BreadcrumbBar sender, BreadcrumbBarItemClickedEventArgs args)
    {
        if (args.Index == 0 && DataContext is FlowDesk.App.ViewModels.WorkspaceDetailsViewModel vm &&
            vm.BackCommand.CanExecute(null))
            vm.BackCommand.Execute(null);
    }
}
