namespace FlowDesk.App.Services;

internal static class PaletteBackdropPolicy
{
    public static bool UseSolidFallback(bool controllerReady, bool materialActive, bool highContrast, bool advancedEffects)
    {
        // Observe native state without mirroring it with a second surface. The controller owns its
        // own FallbackColor/transition, including battery saver and the ~25 ms Fallback on show.
        // An extra XAML cover is needed only for explicit system policy or a missing controller.
        // Intentionally independent of materialActive in BOTH hidden/opening and steady-state cases:
        // this avoids timers, stale callbacks on reopen and a late second flash for persistent fallback.
        _ = materialActive;
        return !controllerReady || highContrast || !advancedEffects;
    }
}
