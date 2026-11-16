using FlowDesk.App.Services;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI.ViewManagement;

namespace FlowDesk.App.Views;

/// <summary>One transient palette target, with Thin Acrylic and explicit shown/theme state.</summary>
internal sealed class PaletteAcrylicBackdrop : SystemBackdrop
{
    private readonly UISettings settings = new();
    private readonly SystemBackdropConfiguration configuration = new();
    private DesktopAcrylicController? controller;
    private ICompositionSupportsSystemBackdrop? target;
    private XamlRoot? root;
    private bool shown;
    private ElementTheme theme;
    private long epoch;
    private string? failure;
    public event Action? StateChanged;

    public bool UseSolidFallback => PaletteBackdropPolicy.UseSolidFallback(controller is not null,
        controller?.State == SystemBackdropState.Active, configuration.IsHighContrast, settings.AdvancedEffectsEnabled);

    public void Update(bool isShown, ElementTheme actualTheme)
    {
        shown = isShown; theme = actualTheme;
        Synchronize();
    }

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(connectedTarget, xamlRoot);
        target = connectedTarget; root = xamlRoot; Interlocked.Increment(ref epoch); failure = null;
        settings.AdvancedEffectsEnabledChanged += EffectsChanged;
        if (xamlRoot.Content is FrameworkElement content) theme = content.ActualTheme;
        try
        {
            if (DesktopAcrylicController.IsSupported())
            {
                controller = new DesktopAcrylicController { Kind = DesktopAcrylicKind.Thin };
                controller.StateChanged += ControllerStateChanged;
                controller.SetSystemBackdropConfiguration(configuration);
                if (!controller.AddSystemBackdropTarget(connectedTarget))
                    throw new InvalidOperationException("Palette backdrop target could not be connected.");
            }
        }
        catch (Exception ex) { failure = ex.GetType().Name; ReleaseController(); }
        Synchronize();
    }

    protected override void OnDefaultSystemBackdropConfigurationChanged(ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
    {
        base.OnDefaultSystemBackdropConfigurationChanged(target, xamlRoot);
        Synchronize(); // retain framework high-contrast colours, not its activation policy
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        Interlocked.Increment(ref epoch);
        settings.AdvancedEffectsEnabledChanged -= EffectsChanged;
        target = null; root = null;
        ReleaseController();
        base.OnTargetDisconnected(disconnectedTarget);
    }

    private void Synchronize()
    {
        if (target is null || root is null) return;
        var defaults = GetDefaultSystemBackdropConfiguration(target, root);
        configuration.IsInputActive = shown;
        configuration.Theme = theme switch
        {
            ElementTheme.Dark => SystemBackdropTheme.Dark,
            ElementTheme.Light => SystemBackdropTheme.Light,
            _ => defaults.Theme
        };
        configuration.HighContrastBackgroundColor = defaults.HighContrastBackgroundColor;
        configuration.IsHighContrast = defaults.IsHighContrast;
        StateChanged?.Invoke();
    }

    // WinRT notifications may arrive off the UI thread or after detachment. Never update XAML
    // there, and never let an old connection's queued callback change a reconnected palette.
    private void EffectsChanged(UISettings sender, object args) => Post(synchronize: true);
    private void ControllerStateChanged(ISystemBackdropControllerWithTargets sender, object args) => Post(synchronize: false);
    private void Post(bool synchronize)
    {
        long expected = Interlocked.Read(ref epoch);
        DispatcherQueue.TryEnqueue(() =>
        {
            if (expected != epoch || target is null) return;
            if (synchronize) Synchronize(); else StateChanged?.Invoke();
        });
    }

    private void ReleaseController()
    {
        var old = controller; controller = null;
        if (old is null) return;
        old.StateChanged -= ControllerStateChanged;
        old.Dispose(); // removes its targets and releases native composition resources
    }

    public string Describe() => $"kind=Thin controller={controller is not null} state={controller?.State.ToString() ?? "unavailable"} " +
        $"inputActive={configuration.IsInputActive} theme={configuration.Theme} highContrast={configuration.IsHighContrast} " +
        $"solidFallback={UseSolidFallback} failure={failure ?? "none"}";
}
