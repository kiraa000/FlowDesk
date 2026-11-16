namespace FlowDesk.App.Services;

internal static class PaletteBackdropPolicy
{
    public static bool UseSolidFallback(bool controllerReady, bool materialActive, bool highContrast, bool advancedEffects) =>
        !controllerReady || !materialActive || highContrast || !advancedEffects;
}
