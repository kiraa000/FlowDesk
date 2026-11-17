using System.Diagnostics;
using System.ComponentModel;
using FlowDesk.Core.Advanced;
using Microsoft.Win32;

namespace FlowDesk.App.Services;

/// <summary>Read-only snapshot. No winget detection processes, registry writes or directory scans.</summary>
public sealed class WindowsToolReader : IToolDetectionReader
{
    private readonly List<ToolRegistration> registrations = [];
    private readonly Dictionary<string, List<string>> appPaths = new(StringComparer.OrdinalIgnoreCase);
    public WindowsToolReader()
    {
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var uninstall = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall");
                foreach (var name in uninstall?.GetSubKeyNames() ?? [])
                {
                    try
                    {
                        using var key = uninstall!.OpenSubKey(name);
                        if (key?.GetValue("DisplayName") is string display)
                            registrations.Add(new(display, key.GetValue("InstallLocation") as string, key.GetValue("DisplayIcon") as string));
                    }
                    catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
                }
                foreach (var tool in AdvancedTools.All)
                {
                    using var key = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\App Paths\" + tool.Executable);
                    if (key?.GetValue("") is string path)
                    {
                        if (!appPaths.TryGetValue(tool.Executable, out var list)) appPaths[tool.Executable] = list = [];
                        list.Add(path);
                    }
                }
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
        }
    }
    public IEnumerable<ToolRegistration> UninstallEntries() => registrations;
    public IEnumerable<string> AppPaths(string executable) => appPaths.GetValueOrDefault(executable) ?? [];
    public IEnumerable<string> InstallRoots() => new[] { Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) }.Where(p => !string.IsNullOrEmpty(p));
    public bool FileExists(string path) => File.Exists(path);
}

public sealed record ToolSnapshot(Dictionary<string, string?> Paths, string? WingetPath);
public sealed class AdvancedToolService(string logFolder)
{
    public Task<ToolSnapshot> DetectAsync() => Task.Run(() =>
    {
        var reader = new WindowsToolReader();
        return new ToolSnapshot(AdvancedTools.All.ToDictionary(t => t.Id, t => ToolDetection.Find(t, reader)), FindWinget());
    });
    private static string? FindWinget()
    {
        foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            try { var path = Path.Combine(folder.Trim('"'), "winget.exe"); if (Path.IsPathFullyQualified(path) && File.Exists(path)) return path; }
            catch (ArgumentException) { }
        }
        return null;
    }
    public static void Open(AdvancedTool tool, string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("The program is no longer at its detected location. Checking again.");
        using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, Arguments = tool.Arguments });
        if (process is null) throw new InvalidOperationException("Windows could not start the program.");
    }
    public static void OpenLink(string uri) { using var process = Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); }
    public static void RunWinUtil()
    {
        using var process = Process.Start(ToolLaunch.WinUtil(Environment.GetFolderPath(Environment.SpecialFolder.Windows)));
        if (process is null) throw new InvalidOperationException("Windows could not start PowerShell.");
    }

    /// <summary>Cancellation stops observation, never kills an installer. The drain/log task retains the process
    /// until exit; normal UI quit instead waits for this task so pipes remain open. No Kill/kill-tree on disposal.</summary>
    public Task<int> InstallAsync(string winget, AdvancedTool tool, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return RunInstallAsync(winget, tool).WaitAsync(cancellationToken);
    }
    private async Task<int> RunInstallAsync(string winget, AdvancedTool tool)
    {
        using var process = new Process { StartInfo = ToolLaunch.Winget(winget, tool.Id) };
        var tail = new Queue<string>();
        var gate = new object();
        async Task Drain(StreamReader stream)
        {
            // Fixed-size reads also bound memory when winget uses CR progress without newlines.
            char[] buffer = new char[1024];
            int count;
            while ((count = await stream.ReadAsync(buffer)) > 0)
            {
                lock (gate)
                {
                    tail.Enqueue(new string(buffer, 0, count));
                    while (tail.Count > 16) tail.Dequeue();
                }
            }
        }
        if (!process.Start()) throw new InvalidOperationException("App Installer could not start.");
        var output = Drain(process.StandardOutput); var error = Drain(process.StandardError);
        await process.WaitForExitAsync();
        await Task.WhenAll(output, error);
        try
        {
            Directory.CreateDirectory(logFolder);
            // Bounded output tail, no command input or environment values recorded.
            await File.AppendAllTextAsync(Path.Combine(logFolder, "advanced-tools.log"),
                $"{DateTimeOffset.UtcNow:O} {tool.Id} exit 0x{unchecked((uint)process.ExitCode):X8}\n" + string.Concat(tail) + "\n");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return process.ExitCode;
    }
    public static bool IsUacDeclined(Exception ex) => ex is Win32Exception { NativeErrorCode: 1223 };
}
