using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using FlowDesk.App.Services;
using FlowDesk.App.ViewModels;
using CommunityToolkit.Mvvm.Input;

namespace FlowDesk.App.Views;

public sealed partial class AdvancedPage : Page
{
    private DialogFocusReturn? pendingDialogFocus;
    public AdvancedPage()
    {
        InitializeComponent();
        DataContext = App.ViewModels.Advanced;
        Loaded += async (_, _) => await App.ViewModels.Advanced.RefreshAsync();
    }
    internal DialogFocusReturn? TakeDialogFocusReturn()
    {
        var target = pendingDialogFocus;
        pendingDialogFocus = null;
        return target;
    }
    private async void InstallTool_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ToolCardViewModel card } button)
            await InvokeWithFocusAsync(button, card.InstallCommand);
    }
    private async void RunWinUtil_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button) await InvokeWithFocusAsync(button, App.ViewModels.Advanced.RunWinUtilCommand);
    }
    private async Task InvokeWithFocusAsync(Button button, IAsyncRelayCommand command)
    {
        if (!command.CanExecute(null)) return;
        var target = new DialogFocusReturn(this, button);
        pendingDialogFocus = target; // Capture before ExecuteAsync can disable or hide the button.
        try { await command.ExecuteAsync(null); }
        catch (Exception ex) { App.ViewModels.ShowStatus("The tool action could not complete: " + ex.Message, StatusKind.Warning); }
        finally
        {
            // A confirmation consumes this target before awaiting its dialog. An unrelated card can
            // open another dialog while winget runs; never clear that newer request here.
            if (ReferenceEquals(pendingDialogFocus, target)) pendingDialogFocus = null;
        }
    }
}
