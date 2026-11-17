using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

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

    // Undo disappears when its journal entry is consumed. Move focus before that happens,
    // rather than leaving WinUI to fall back to NavigationView's first focusable control.
    private void UndoAll_Click(object sender, RoutedEventArgs e)
    {
        if (!App.ViewModels.Calm.Busy) CalmAllButton.Focus(FocusState.Programmatic);
    }

    private void UndoRow_Click(object sender, RoutedEventArgs e)
    {
        if (App.ViewModels.Calm.Busy) return;
        if (sender is not HyperlinkButton undo || VisualTreeHelper.GetParent(undo) is not DependencyObject row) return;
        var target = FindInput(row);
        if (target is not null) target.Focus(FocusState.Programmatic);
        else CalmAllButton.Focus(FocusState.Programmatic);
    }

    private static Control? FindInput(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is Control control && (child is ToggleSwitch or ComboBox) &&
                control.IsEnabled && control.Visibility == Visibility.Visible) return control;
            if (child is UIElement element && element.Visibility == Visibility.Collapsed) continue;
            if (FindInput(child) is { } nested) return nested;
        }
        return null;
    }
}
