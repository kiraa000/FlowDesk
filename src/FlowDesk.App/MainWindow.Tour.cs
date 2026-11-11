using FlowDesk.App.Tour;
using FlowDesk.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace FlowDesk.App;

// Onboarding tour shell: one TeachingTip open at a time, real page/shell targets, Back / Next / Skip.
// The step list and movement rules live in the plain TourStateMachine.
public sealed partial class MainWindow
{
    private bool tourActive;
    private TaskCompletionSource<bool>? tourTipClosed;
    private int tourGeneration;

    private void InitializeTour()
    {
        App.ViewModels.TourRequested += OnTourRequested;
        Root.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(OnTourKeyDown), handledEventsToo: true);
    }

    private async void OnTourRequested()
    {
        // Invalidate old navigation immediately; reopening must wait for the actual Closed event,
        // not an assumed animation duration (animations/accessibility settings can change it).
        int token = ++tourGeneration;
        tourActive = false;
        await CloseTourTipAsync();
        if (token != tourGeneration) return;
        tourActive = true;
        await ShowCurrentTourStepAsync(token);
    }

    private async Task ShowCurrentTourStepAsync(int token)
    {
        var tour = App.ViewModels.Tour;
        // Stale async work must never cancel a newer tour generation.
        if (token != tourGeneration) return;
        if (!tourActive || !tour.IsRunning) { HideTour(); return; }

        var step = tour.Current;
        if (step.Page is { } page) Navigate(page switch
        {
            TourPage.Layouts => AppPage.Layouts,
            TourPage.Focus => AppPage.Focus,
            TourPage.Settings => AppPage.Settings,
            _ => AppPage.Workspaces,
        });

        FrameworkElement? target = null;
        if (step.TargetId is { } targetId) target = await WaitForTourTargetAsync(targetId, token);
        if (!tourActive || token != tourGeneration || !tour.IsRunning) return;

        bool usingEmptyState = target is not null && target == PageElement("EmptyStateButton");
        TourBack.IsEnabled = !tour.IsFirst;
        TourNext.Content = tour.IsLast ? "Finish" : "Next";
        TourStepText.Text = tour.StepLabel;
        TourTip.Title = step.Title;
        TourTip.Subtitle = usingEmptyState && step.FallbackBody is not null ? step.FallbackBody : step.Body;
        TourTip.PreferredPlacement = target is null ? TeachingTipPlacementMode.Center
            : target is NavigationViewItem ? TeachingTipPlacementMode.Right
            : TeachingTipPlacementMode.Bottom;
        TourTip.Target = target;
        TourTip.IsOpen = true;
        // Focus moves into the tip so keyboard users can walk it with Tab/Enter and leave with Escape.
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (tourActive && token == tourGeneration) TourNext.Focus(FocusState.Keyboard);
        });
    }

    /// <summary>Page loads and item templates are async; wait briefly for a real, visible target.</summary>
    private async Task<FrameworkElement?> WaitForTourTargetAsync(string targetId, int token)
    {
        for (int attempt = 0; attempt < 60; attempt++)
        {
            var target = FindTourTarget(targetId);
            if (IsUsable(target)) return target;
            if (!tourActive || token != tourGeneration || !App.ViewModels.Tour.IsRunning) return null;
            await Task.Delay(50);
        }
        return FindTourTarget(targetId);
    }

    private FrameworkElement? FindTourTarget(string targetId) => targetId switch
    {
        "LayoutsNav" => FindNavItem("Layouts"),
        "FocusNav" => FindNavItem("Focus"),
        "SettingsNav" => Navigation.SettingsItem as FrameworkElement,
        "NewWorkspace" => IsUsable(PageElement("NewWorkspace")) ? PageElement("NewWorkspace") : PageElement("EmptyStateButton"),
        "OpenWorkspace" => FindFirstOpenButton() ?? PageElement("EmptyStateButton"),
        _ => null,
    };

    private FrameworkElement? FindNavItem(string tag) =>
        Navigation.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(item => item.Tag?.ToString() == tag);

    private FrameworkElement? PageElement(string name) =>
        (PageFrame.Content as FrameworkElement)?.FindName(name) as FrameworkElement;

    private Button? FindFirstOpenButton()
    {
        if (PageFrame.Content is not DependencyObject root) return null;
        return Descendants<Button>(root)
            .FirstOrDefault(button => button.Content is string text && text == "Open" && IsUsable(button));
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private static bool IsUsable(FrameworkElement? element) =>
        element is { IsLoaded: true, Visibility: Visibility.Visible, ActualWidth: > 0, ActualHeight: > 0 };

    private async void TourBack_Click(object sender, RoutedEventArgs e)
    {
        if (!tourActive) return;
        App.ViewModels.Tour.Back();
        await ShowCurrentTourStepAsync(++tourGeneration);
    }

    private async void TourNext_Click(object sender, RoutedEventArgs e)
    {
        if (!tourActive) return;
        var tour = App.ViewModels.Tour;
        bool wasLast = tour.IsLast;
        tour.Next();
        if (wasLast) HideTour();
        else await ShowCurrentTourStepAsync(++tourGeneration);
    }

    private void TourTip_CloseButtonClick(TeachingTip sender, object args)
    {
        if (tourActive) App.ViewModels.Tour.Skip();
        HideTour();
    }

    private void TourTip_Closed(TeachingTip sender, object args)
    {
        if (tourTipClosed is { } completion)
        {
            tourTipClosed = null;
            completion.TrySetResult(true);
            return;
        }
        if (!tourActive) return;
        App.ViewModels.Tour.Skip();
        HideTour();
    }

    private void OnTourKeyDown(object sender, KeyRoutedEventArgs e) => HandleTourEscape(e);

    // TeachingTip content lives in a popup, so Escape needs its own handler there as well as on the root.
    private void TourContent_KeyDown(object sender, KeyRoutedEventArgs e) => HandleTourEscape(e);

    private void HandleTourEscape(KeyRoutedEventArgs e)
    {
        if (!tourActive || e.Key != VirtualKey.Escape) return;
        e.Handled = true;
        App.ViewModels.Tour.Skip();
        HideTour();
    }

    /// <summary>Closes the tip without marking the tour complete (window close or a new tour start).</summary>
    private void HideTour()
    {
        tourActive = false;
        tourGeneration++;
        _ = CloseTourTipAsync();
        TourTip.Target = null;
    }

    /// <summary>All replay requests share the pending close; Closed cannot skip a newer run.</summary>
    private Task CloseTourTipAsync()
    {
        if (tourTipClosed is { } pending) return pending.Task;
        if (!TourTip.IsOpen) return Task.CompletedTask;
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        tourTipClosed = completion;
        TourTip.IsOpen = false;
        return completion.Task;
    }

    private void StopTour() => HideTour();
}
