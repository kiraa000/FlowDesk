using System.Security;
using Microsoft.Win32;

namespace FlowDesk.App.Services;

/// <summary>
/// "Start FlowDesk when I sign in": one HKCU Run value holding the quoted path of the running exe.
/// The UI reads the real registry state on load; this class never caches a preference.
/// </summary>
public static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "FlowDesk";

    /// <summary>The command written to the Run value: the current executable, quoted for paths with spaces.</summary>
    public static string Command
    {
        get
        {
            string exe = Environment.ProcessPath
                ?? Path.Combine(AppContext.BaseDirectory, "FlowDesk.App.exe");
            return $"\"{exe}\"";
        }
    }

    /// <summary>True when the Run value exists; this is the state the Settings toggle shows on load.</summary>
    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException)
        { return false; }
    }

    /// <summary>Adds or removes the Run value. Returns false when Windows refused the change.</summary>
    public static bool SetEnabled(bool enabled)
    {
        try
        {
            if (!enabled)
            {
                using var existing = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
                existing?.DeleteValue(ValueName, throwOnMissingValue: false);
                return true;
            }
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (key is null) return false;
            key.SetValue(ValueName, Command, RegistryValueKind.String);
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException)
        { return false; }
    }
}
