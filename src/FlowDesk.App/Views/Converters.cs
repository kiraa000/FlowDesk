using FlowDesk.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI;

namespace FlowDesk.App.Views;

public sealed class BooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        value is Visibility.Visible;
}
public sealed class StatusKindConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) => value switch
    {
        StatusKind.Error => InfoBarSeverity.Error, StatusKind.Warning => InfoBarSeverity.Warning,
        StatusKind.Success => InfoBarSeverity.Success, _ => InfoBarSeverity.Informational
    };
    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
public sealed class TagColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        // User-picked workspace colours only. Labels accompany them; high contrast uses theme text.
        if (new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast)
            return Application.Current.Resources["TextFillColorPrimaryBrush"];
        return new SolidColorBrush((value as string) switch
        {
            "green" => Colors.SeaGreen, "orange" => Colors.DarkOrange,
            "red" => Colors.IndianRed, _ => Colors.SteelBlue
        });
    }
    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
