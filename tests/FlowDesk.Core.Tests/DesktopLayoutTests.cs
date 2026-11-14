using FlowDesk.Core.Layout;
using FlowDesk.Core.Windows;

namespace FlowDesk.Core.Tests;

/// <summary>
/// Layouts keep the old local behaviour: windows on other virtual desktops are ignored even when a
/// caller passes them in (capture now sees every desktop). Unknown metadata behaves as before.
/// </summary>
public class DesktopLayoutTests
{
    private const string Current = @"C:\apps\current.exe";
    private const string Other = @"C:\apps\other.exe";
    private const string Unknown = @"C:\apps\unknown.exe";

    private readonly FakeWindowSystem _sys = new();

    [Fact]
    public void Layout_only_arranges_windows_on_the_current_desktop()
    {
        var current = _sys.Add(Current, "Current", isOnCurrentDesktop: true);
        var other = _sys.Add(Other, "Other", isOnCurrentDesktop: false);
        var unknown = _sys.Add(Unknown, "Unknown"); // old backends: unknown keeps old behaviour

        var results = new LayoutApplier(_sys).Apply(LayoutPreset.TwoColumns, [other, current, unknown]);

        Assert.Equal(2, results.Count);
        Assert.DoesNotContain(results, r => r.Handle == other.Handle);
        Assert.Contains(_sys.Placed, p => p.Handle == current.Handle);
        Assert.Contains(_sys.Placed, p => p.Handle == unknown.Handle);
        Assert.DoesNotContain(_sys.Placed, p => p.Handle == other.Handle);
        Assert.DoesNotContain(_sys.Fronted, h => h == other.Handle);
    }

    [Fact]
    public void Layout_with_only_off_desktop_windows_places_nothing()
    {
        _sys.Add(Other, "Other", isOnCurrentDesktop: false);

        var results = new LayoutApplier(_sys).Apply(LayoutPreset.Grid, _sys.GetAllAppWindows());

        Assert.Empty(results);
        Assert.Empty(_sys.Placed);
        Assert.Empty(_sys.Fronted);
    }

    [Fact]
    public void Unknown_current_plus_cloak_is_not_treated_as_local()
    {
        var cloaked = _sys.Add(Other, "Cloaked", isOnCurrentDesktop: null, cloakReason: 2);

        var results = new LayoutApplier(_sys).Apply(LayoutPreset.Grid, [cloaked]);

        Assert.Empty(results);
        Assert.Empty(_sys.Placed);
        Assert.Empty(_sys.Fronted);
    }

    [Fact]
    public void Known_current_window_with_an_app_cloak_stays_layout_eligible()
    {
        // Suspended UWP frames on the current desktop are DWM_CLOAKED_APP; they keep the old behaviour.
        var suspended = _sys.Add(Unknown, "Suspended", isOnCurrentDesktop: true, cloakReason: 1);

        var results = new LayoutApplier(_sys).Apply(LayoutPreset.Grid, [suspended]);

        Assert.Single(results);
        Assert.Equal(suspended.Handle, results[0].Handle);
        Assert.Contains(_sys.Placed, p => p.Handle == suspended.Handle);
        Assert.Contains(suspended.Handle, _sys.Fronted);
    }
}
