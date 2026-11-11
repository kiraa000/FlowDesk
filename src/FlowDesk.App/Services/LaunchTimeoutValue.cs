// Pure validation for the Settings launch timeout. No WinUI dependency, so the rule is unit tested headlessly.
namespace FlowDesk.App.Services;

public static class LaunchTimeoutValue
{
    public const int Min = 10;
    public const int Max = 60;
    public const int Default = 20;

    /// <summary>Clamps a persisted value into the supported range.</summary>
    public static int Clamp(int seconds) => Math.Clamp(seconds, Min, Max);

    /// <summary>
    /// Turns a NumberBox value into a valid setting. NaN/infinity (cleared input) and fractional values
    /// fall back to a clamped <paramref name="fallback"/>, normally the last valid setting.
    /// </summary>
    public static int Normalize(double value, int fallback = Default)
    {
        int safeFallback = Clamp(fallback);
        if (!double.IsFinite(value)) return safeFallback;
        // Range-check before the cast: converting an out-of-range double to int is not a clamp.
        if (value <= Min) return Min;
        if (value >= Max) return Max;
        return (int)Math.Round(value);
    }
}
