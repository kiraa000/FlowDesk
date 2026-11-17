using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using FlowDesk.Core.Calm;
using Microsoft.Win32;

namespace FlowDesk.App.Services;

public sealed class WindowsRegistryAccess : IRegistryAccess
{
    private static RegistryKey Root(RegistryAddress address) => address.Machine ? Registry.LocalMachine : Registry.CurrentUser;
    public RegistryValue? Read(RegistryAddress address)
    {
        using var key = Root(address).OpenSubKey(address.Path);
        if (key is null || !key.GetValueNames().Contains(address.Name, StringComparer.OrdinalIgnoreCase)) return null;
        object? value = key.GetValue(address.Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        return new((int)key.GetValueKind(address.Name), JsonSerializer.Serialize(value, value?.GetType() ?? typeof(object)));
    }
    public IReadOnlyList<string> ValueNames(string path)
    {
        using var key = Registry.CurrentUser.OpenSubKey(path);
        return key?.GetValueNames() ?? [];
    }
    public bool KeyExists(string path)
    {
        using var key = Registry.CurrentUser.OpenSubKey(path);
        return key is not null;
    }
    public void DeleteKeyIfEmpty(string path)
    {
        int separator = path.LastIndexOf('\\');
        if (separator <= 0) throw new InvalidDataException("A root key cannot be removed.");
        using var parent = Registry.CurrentUser.OpenSubKey(path[..separator], true);
        if (parent is null) return;
        using (var key = parent.OpenSubKey(path[(separator + 1)..]))
        {
            if (key is null || key.ValueCount != 0 || key.SubKeyCount != 0) return;
        }
        // DeleteSubKey (never DeleteSubKeyTree) also refuses a newly-added child key.
        parent.DeleteSubKey(path[(separator + 1)..], false);
    }
    public void Write(RegistryAddress address, RegistryValue value)
    {
        if (address.Machine) throw new InvalidOperationException("Calm only changes per-user settings.");
        object data = (RegistryValueKind)value.Kind switch
        {
            RegistryValueKind.DWord => JsonSerializer.Deserialize<int>(value.Data),
            RegistryValueKind.QWord => JsonSerializer.Deserialize<long>(value.Data),
            RegistryValueKind.Binary or RegistryValueKind.None => JsonSerializer.Deserialize<byte[]>(value.Data)!,
            RegistryValueKind.MultiString => JsonSerializer.Deserialize<string[]>(value.Data)!,
            RegistryValueKind.String or RegistryValueKind.ExpandString => JsonSerializer.Deserialize<string>(value.Data)!,
            _ => throw new InvalidDataException("Unsupported registry value type.")
        };
        using var key = Registry.CurrentUser.CreateSubKey(address.Path, true);
        key.SetValue(address.Name, data, (RegistryValueKind)value.Kind);
    }
    public void Delete(RegistryAddress address)
    {
        if (address.Machine) throw new InvalidOperationException("Calm only changes per-user settings.");
        using var key = Registry.CurrentUser.OpenSubKey(address.Path, true);
        key?.DeleteValue(address.Name, false);
    }
}
public sealed class WindowsStartupFolder : IStartupFolder
{
    public IReadOnlyList<StartupFile> Entries()
    {
        string path = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        return Directory.Exists(path) ? Directory.EnumerateFiles(path).Where(p => !Path.GetFileName(p).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase))
            .Select(p => new StartupFile(Path.GetFileName(p), p)).ToArray() : [];
    }
}
public sealed class FileCalmJournalStore(string folder) : ICalmJournalStore
{
    private readonly string path = Path.Combine(folder, "calm-undo.json");
    public CalmJournal Load() => File.Exists(path) ? CalmJournal.FromJson(File.ReadAllText(path)) : new();
    public void Save(CalmJournal journal)
    {
        Directory.CreateDirectory(folder);
        string temp = path + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        using (var writer = new StreamWriter(stream)) { writer.Write(journal.ToJson()); writer.Flush(); stream.Flush(true); }
        if (File.Exists(path)) File.Replace(temp, path, path + ".bak", true);
        else File.Move(temp, path);
    }
}
internal static class CalmWindows
{
    internal static WindowsBuild Build(IRegistryAccess registry)
    {
        int revision = registry.Read(new(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "UBR", true))?.AsDword() ?? 0;
        return new(Environment.OSVersion.Version.Build, revision);
    }
    internal static string ActiveHours(IRegistryAccess registry)
    {
        try
        {
            const string path = @"SOFTWARE\Microsoft\WindowsUpdate\UX\Settings";
            int? start = registry.Read(new(path, "ActiveHoursStart", true))?.AsDword();
            int? end = registry.Read(new(path, "ActiveHoursEnd", true))?.AsDword();
            return start is >= 0 and <= 23 && end is >= 0 and <= 23 ? $"Active hours {start:00}:00–{end:00}:00. Managed in Windows Settings." : "Managed in Windows Settings.";
        }
        catch { return "Managed in Windows Settings."; }
    }
    internal static Task BroadcastAsync(IEnumerable<string> areas) => Task.Run(() =>
    {
        foreach (string area in areas.Distinct()) SendMessageTimeout(new nint(0xffff), 0x1a, 0, area, 0x2 | 0x20, 200, out _);
    });
    internal static Task RestartExplorerAsync() => Task.Run(async () =>
    {
        // Target only this session's actual shell, never every explorer.exe (other sessions/folder processes).
        nint shell = GetShellWindow();
        if (shell == 0) throw new InvalidOperationException("File Explorer is not the current shell.");
        GetWindowThreadProcessId(shell, out uint pid);
        using var process = Process.GetProcessById(checked((int)pid));
        string explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        if (process.SessionId != Process.GetCurrentProcess().SessionId || !string.Equals(process.MainModule?.FileName, explorer, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The current shell could not be verified.");
        if (GetShellWindow() != shell) throw new InvalidOperationException("The shell changed. Try again.");
        process.Kill();
        bool replacement = await ExplorerRestartWait.WaitAsync(pid, ShellProcessId, Task.Delay);
        if (replacement) return; // AutoRestartShell normally restores it; launching again opens a folder.
        // Recheck immediately before fallback. If ANY shell is present, never launch another Explorer.
        if (GetShellWindow() != 0) return;
        if (!process.HasExited) throw new InvalidOperationException("File Explorer has not exited. Try again.");
        Process.Start(new ProcessStartInfo(explorer) { UseShellExecute = true });
    });
    private static uint? ShellProcessId()
    {
        nint shell = GetShellWindow();
        if (shell == 0) return null;
        GetWindowThreadProcessId(shell, out uint pid);
        return pid == 0 ? null : pid;
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint SendMessageTimeout(nint hwnd, uint msg, nint wparam, string lparam, uint flags, uint timeout, out nint result);
    [DllImport("user32.dll")] private static extern nint GetShellWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
}
