using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
namespace FlowDesk.App.Views;
internal static class Ui
{
    public static TextBlock Text(string text, string style = "BodyTextBlockStyle", bool secondary = false)
    {
        // XAML theme references remain live when RequestedTheme changes at runtime.
        var brush = secondary ? "TextFillColorSecondaryBrush" : "TextFillColorPrimaryBrush";
        var markup = "<TextBlock xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' "
            + $"Style='{{StaticResource {style}}}' Foreground='{{ThemeResource {brush}}}' TextWrapping='Wrap' />";
        var block = (TextBlock)Microsoft.UI.Xaml.Markup.XamlReader.Load(markup);
        block.Text = text; return block;
    }
    public static StackPanel Stack(double spacing = 12) => new() { Spacing = spacing };
    public static StackPanel Row() => new() { Orientation = Orientation.Horizontal, Spacing = 8 };
    public static Button Button(string text, Action action, bool accent = false)
    {
        var b = new Button { Content = text, MinHeight = 32 };
        if (accent) b.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
        b.Click += (_, _) => action(); return b;
    }
    public static FontIcon Glyph(string glyph) => new() { Glyph = glyph };
    public static ScrollViewer Scroll(UIElement content) => new()
    {
        Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
    };
}
