using System.Diagnostics;
using System.Security.Principal;

namespace FlowDesk.App.Services;

/// <summary>No shell command parsing, arguments, or elevation verbs. Resolution is independently testable.</summary>
public static class TerminalProgramResolver
{
    public static string? Resolve(string? custom, string localAppData, string systemDirectory,
        IEnumerable<string> searchPath, Func<string, bool> exists)
    {
        string program = (custom ?? "").Trim();
        if (program.Length == 0)
        {
            string wt = Path.Combine(localAppData, "Microsoft", "WindowsApps", "wt.exe");
            if (exists(wt)) return wt;
            string cmd = Path.Combine(systemDirectory, "cmd.exe");
            return exists(cmd) ? cmd : null;
        }
        program = Environment.ExpandEnvironmentVariables(program);
        if (program.StartsWith('"') && program.EndsWith('"') && program.Length > 1) program = program[1..^1];
        if (program.Contains('"')) return null;
        if (Path.IsPathFullyQualified(program)) return exists(program) ? program : null;
        if (program.Contains(Path.DirectorySeparatorChar) || program.Contains(Path.AltDirectorySeparatorChar)) return null;
        if (!Path.HasExtension(program)) program += ".exe";
        foreach (string directory in searchPath)
        {
            if (!Path.IsPathFullyQualified(directory)) continue;
            string path = Path.Combine(directory, program);
            if (exists(path)) return path;
        }
        return null;
    }
}

public sealed class ShortcutTerminal
{
    private string? _failedProgram;

    public void Open(string? custom, Action<string>? notify)
    {
        string key = custom?.Trim() ?? "";
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            if (new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
            {
                if (_failedProgram != key)
                {
                    _failedProgram = key;
                    notify?.Invoke("The terminal was not opened because FlowDesk is elevated. Restart FlowDesk without administrator privileges to open a non-elevated terminal.");
                }
                return;
            }
            string? executable = TerminalProgramResolver.Resolve(key,
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Environment.SystemDirectory,
                (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator), File.Exists);
            if (executable is null) throw new FileNotFoundException();
            // UseShellExecute=false never requests elevation or interprets a command line from Settings.
            using var process = Process.Start(new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            });
            if (process is null) throw new InvalidOperationException();
            _failedProgram = null;
        }
        catch (Exception)
        {
            if (_failedProgram == key) return;
            _failedProgram = key;
            notify?.Invoke("The terminal program could not be opened. Check Terminal program in Settings > Window shortcuts, or choose Use default.");
        }
    }
}
