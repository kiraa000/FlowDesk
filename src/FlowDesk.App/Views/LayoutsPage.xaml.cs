using FlowDesk.App.ViewModels;
using FlowDesk.Core.Layout;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.System;
using Windows.UI.Core;

namespace FlowDesk.App.Views;

public sealed partial class LayoutsPage : Page
{
    public LayoutsPage()
    {
        InitializeComponent();
        DataContext = App.ViewModels.Layouts;
        Loaded += LayoutsPage_Loaded;
    }

    private void LayoutsPage_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is LayoutsViewModel vm && vm.RefreshCommand.CanExecute(null))
            vm.RefreshCommand.Execute(null);
    }

    // Schematic previews only: pure LayoutEngine maths, no window or monitor calls from the view.
    private void PresetCanvas_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Canvas canvas || canvas.Tag is not LayoutPreset preset) return;
        var palette = canvas.Children.OfType<Border>().First();
        foreach (var child in canvas.Children.OfType<Rectangle>().ToArray()) canvas.Children.Remove(child);
        double width = canvas.ActualWidth > 0 ? canvas.ActualWidth : canvas.Width;
        double height = canvas.ActualHeight > 0 ? canvas.ActualHeight : canvas.Height;
        if (double.IsNaN(width) || double.IsNaN(height) || width <= 0 || height <= 0) return;
        int count = preset switch { LayoutPreset.TwoColumns => 2, LayoutPreset.Grid => 4, _ => 3 };
        foreach (var r in LayoutEngine.Compute(preset, count, width / height))
        {
            var rect = new Rectangle
            {
                Width = Math.Max(2, r.W * width - 2), Height = Math.Max(2, r.H * height - 2),
                StrokeThickness = 1,
            };
            rect.SetBinding(Shape.FillProperty, new Binding { Source = palette, Path = new PropertyPath("Background") });
            rect.SetBinding(Shape.StrokeProperty, new Binding { Source = palette, Path = new PropertyPath("BorderBrush") });
            Canvas.SetLeft(rect, r.X * width + 1);
            Canvas.SetTop(rect, r.Y * height + 1);
            canvas.Children.Add(rect);
        }
    }

    private void Windows_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (DataContext is not LayoutsViewModel vm || sender is not ListView list) return;
        int from = list.SelectedIndex;
        if (from < 0) return;
        bool control = (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
            & CoreVirtualKeyStates.Down) != 0;
        if (!control) return;
        int to = e.Key switch { VirtualKey.Up => from - 1, VirtualKey.Down => from + 1, _ => from };
        if (to == from || to < 0 || to >= vm.Windows.Count) return;
        vm.Windows.Move(from, to);
        list.SelectedIndex = to;
        e.Handled = true;
    }
}
