using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FlowDesk.App.Services;

public sealed class IconSource : DependencyObject
{
    public static readonly DependencyProperty PathProperty = DependencyProperty.RegisterAttached(
        "Path", typeof(string), typeof(IconSource), new PropertyMetadata("", OnPathChanged));
    public static string GetPath(DependencyObject element) => (string)element.GetValue(PathProperty);
    public static void SetPath(DependencyObject element, string value) => element.SetValue(PathProperty, value);
    private static void OnPathChanged(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        if (element is Image image) _ = Icons.LoadAsync(image, args.NewValue as string);
    }
}
