using FlowDesk.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FlowDesk.App.Views;

public sealed partial class FocusPage : Page
{
    public FocusPage()
    {
        InitializeComponent();
        DataContext = App.ViewModels.Focus;
        Loaded += (_, _) => App.ViewModels.Focus.RefreshIconsCommand.Execute(null);
    }

    // Rows are plain records, so the page-level view model owns removal.
    private void RemoveBlocked_Click(object sender, RoutedEventArgs e) => Remove(sender, blocked: true);
    private void RemoveAllowed_Click(object sender, RoutedEventArgs e) => Remove(sender, blocked: false);
    private void Remove(object sender, bool blocked)
    {
        if (sender is not FrameworkElement { DataContext: FocusRuleRow row } ||
            DataContext is not FocusViewModel vm) return;
        var command = blocked ? vm.RemoveBlockedCommand : vm.RemoveAllowedCommand;
        if (command.CanExecute(row)) command.Execute(row);
    }
}
