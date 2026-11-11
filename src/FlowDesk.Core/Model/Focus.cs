namespace FlowDesk.Core.Model;

/// <summary>How hard Focus Mode pushes back when a blocked app shows a window.</summary>
public enum FocusStrictness
{
    /// <summary>Warn once per window; the app keeps running.</summary>
    Gentle,
    /// <summary>Close the window; the user may allow the app for 5 minutes.</summary>
    Normal,
    /// <summary>Close the window every time, no exceptions until the session ends.</summary>
    Strict,
}

/// <summary>An app rule in a focus profile, matched by exe name or AUMID.</summary>
public sealed class FocusAppRule
{
    /// <summary>Display name for the UI.</summary>
    public string Name { get; set; } = "";
    /// <summary>Exe file name without extension, lower case ("discord"). Matches any path.</summary>
    public string? ExeName { get; set; }
    /// <summary>Packaged app id; matched instead of the exe when set.</summary>
    public string? Aumid { get; set; }

    public bool Matches(string? exePath, string? aumid)
    {
        if (!string.IsNullOrEmpty(Aumid))
            return string.Equals(Aumid, aumid, StringComparison.OrdinalIgnoreCase);
        return !string.IsNullOrEmpty(ExeName) && AppKeys.ExeName(exePath) == ExeName.ToLowerInvariant();
    }
}

/// <summary>Settings for a focus session (persisted as the user's last choice).</summary>
public sealed class FocusSettings
{
    public int DurationMinutes { get; set; } = 25;
    public FocusStrictness Strictness { get; set; } = FocusStrictness.Normal;
    public List<FocusAppRule> Blocked { get; set; } = [];
    /// <summary>Always allowed; wins over <see cref="Blocked"/>.</summary>
    public List<FocusAppRule> Allowed { get; set; } = [];
    /// <summary>When true, every app not in <see cref="Allowed"/> is blocked (system apps excepted).</summary>
    public bool BlockAllExceptAllowed { get; set; }

    public static readonly int[] DurationChoices = [25, 50, 90];
}
