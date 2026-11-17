using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace FlowDesk.App.Services;

/// <summary>Captured before a command can disable its invoker. Only restores within the original, still-loaded page.</summary>
internal sealed class DialogFocusReturn(Page page, Button invoker)
{
    private readonly FocusState focusState = invoker.FocusState == FocusState.Keyboard ? FocusState.Keyboard : FocusState.Programmatic;
    private readonly XamlRoot xamlRoot = page.XamlRoot;
    private readonly DependencyObject? card = FindCard(invoker, page);

    public void RestoreAfterDialog()
    {
        // Let ContentDialog restore its saved focus and let the command's Cancel/finally path re-enable
        // the invoker first. Do not wait for an accepted install to finish or steal focus on completion.
        page.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (!page.IsLoaded || page.XamlRoot != xamlRoot) return;
            FocusReturnPolicy.TryRestore(Candidates(), control => CanFocus(control) && control.Focus(focusState));
        });
    }
    private IEnumerable<Control> Candidates()
    {
        yield return invoker;
        if (card is null) yield break;
        foreach (var name in new[] { "ToolOpenButton", "ToolAppInstallerButton", "ToolWebsiteLink" })
            if (FindNamed<Control>(card, name) is { } control) yield return control;
    }
    private bool CanFocus(Control control)
    {
        if (!control.IsLoaded || control.XamlRoot != xamlRoot || !control.IsEnabled) return false;
        // A visible descendant of a collapsed/disabled container is not an available fallback.
        for (DependencyObject? node = control; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is UIElement { Visibility: Visibility.Collapsed }) return false;
            if (node is Control { IsEnabled: false }) return false;
            if (ReferenceEquals(node, page)) return true;
        }
        return false;
    }
    private static DependencyObject? FindCard(DependencyObject node, Page page)
    {
        for (DependencyObject? current = node; current is not null && !ReferenceEquals(current, page); current = VisualTreeHelper.GetParent(current))
            if (current is SettingsCard) return current;
        return null;
    }
    internal static T? FindNamed<T>(DependencyObject node, string name) where T : FrameworkElement
    {
        if (node is T element && element.Name == name) return element;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            if (FindNamed<T>(VisualTreeHelper.GetChild(node, i), name) is { } found) return found;
        return null;
    }
}
