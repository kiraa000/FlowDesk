using System.Runtime.InteropServices;

namespace FlowDesk.App.Services;

/// <summary>
/// Makes Win32 popup menus (the tray menu) follow FlowDesk's theme. uxtheme exports these by ordinal only
/// (Windows 10 1903+); they are undocumented but stable and used by Explorer, PowerToys and Notepad++.
/// Best effort: on any failure the menu keeps the light system look.
/// </summary>
internal static class NativeMenuTheme
{
    private enum PreferredAppMode { Default = 0, AllowDark = 1, ForceDark = 2, ForceLight = 3 }

    [DllImport("uxtheme.dll", EntryPoint = "#135")]
    private static extern PreferredAppMode SetPreferredAppMode(PreferredAppMode mode);

    [DllImport("uxtheme.dll", EntryPoint = "#136")]
    private static extern void FlushMenuThemes();

    /// <param name="theme">The app setting: "Light", "Dark", or anything else for the Windows app mode.</param>
    public static void Apply(string? theme)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 18362)) return;
        try
        {
            SetPreferredAppMode(theme switch
            {
                "Dark" => PreferredAppMode.ForceDark,
                "Light" => PreferredAppMode.ForceLight,
                _ => PreferredAppMode.AllowDark,
            });
            FlushMenuThemes();
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException) { }
    }
}
