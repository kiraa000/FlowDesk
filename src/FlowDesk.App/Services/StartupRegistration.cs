using System.Security;
using Microsoft.Win32;
using FlowDesk.Core.Calm;

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
            return $"\"{exe}\" --tray";
        }
    }

    public static bool LaunchHidden(IEnumerable<string> arguments) =>
        arguments.Any(a => string.Equals(a, "--tray", StringComparison.OrdinalIgnoreCase));

    /// <summary>Upgrade an enabled legacy Run entry, including a previous install path; never change approval.</summary>
    public static bool UpgradeLegacyCommand() => UpgradeLegacyCommand(new WindowsRegistryAccess(), Command);
    public static bool UpgradeLegacyCommand(IRegistryAccess registry, string command)
    {
        try
        {
            if (!IsEnabled(registry)) return false;
            var address = new RegistryAddress(RunKeyPath, ValueName);
            var stored = registry.Read(address);
            string? text = stored is null ? null : System.Text.Json.JsonSerializer.Deserialize<string>(stored.Data);
            if (string.IsNullOrWhiteSpace(text) || LaunchHidden(FlowDesk.Core.Restore.BrowserSession.Tokenize(text).Skip(1))) return false;
            registry.Write(address, new((int)RegistryValueKind.String, System.Text.Json.JsonSerializer.Serialize(command)));
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException or System.Text.Json.JsonException)
        { return false; }
    }

    /// <summary>A Run command must exist AND StartupApproved must not disable it.</summary>
    public static bool IsEnabled() => IsEnabled(new WindowsRegistryAccess());
    public static bool IsEnabled(IRegistryAccess registry)
    {
        try
        {
            var command = registry.Read(new(RunKeyPath, ValueName));
            return command is { Kind: 1 or 2 } && !string.IsNullOrWhiteSpace(System.Text.Json.JsonSerializer.Deserialize<string>(command.Data))
                && StartupApprovedCodec.Enabled(registry.Read(new(StartupCatalog.Approved + "Run", ValueName))) == true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException or System.Text.Json.JsonException)
        { return false; }
    }

    /// <summary>Adds or removes the Run value. Returns false when Windows refused the change.</summary>
    public static bool SetEnabled(bool enabled) => SetEnabled(enabled, new WindowsRegistryAccess(), Command);
    public static bool SetEnabled(bool enabled, IRegistryAccess registry, string command)
    {
        try
        {
            if (!enabled)
            {
                registry.Delete(new(RunKeyPath, ValueName));
                return true;
            }
            var approval = new RegistryAddress(StartupCatalog.Approved + "Run", ValueName);
            var previous = registry.Read(approval);
            if (StartupApprovedCodec.Enabled(previous) is null) return false;
            // Enabling from Settings must also reverse a Task Manager / Calm disable.
            if (previous is not null) registry.Write(approval, StartupApprovedCodec.Encode(true, previous));
            registry.Write(new(RunKeyPath, ValueName), new((int)RegistryValueKind.String, System.Text.Json.JsonSerializer.Serialize(command)));
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException)
        { return false; }
    }
}
