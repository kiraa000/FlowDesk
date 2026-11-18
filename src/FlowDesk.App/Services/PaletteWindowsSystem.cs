using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using FlowDesk.Core.Palette;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace FlowDesk.App.Services;

internal sealed class PaletteWindowsSystem(AppServices services) : IPaletteSystem
{
    private const string Personalize = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public async Task<SystemPaletteState> ReadAsync()
    {
        var dnd = services.DoNotDisturb.ReadAsync();
        var mute = services.AudioOutput is AudioOutputService audio ? audio.ReadMuteAsync() : Task.FromResult<bool?>(null);
        var bin = new RecycleBinInfo { Size = Marshal.SizeOf<RecycleBinInfo>() };
        bool binAvailable = SHQueryRecycleBinW(null, ref bin) >= 0;
        using var theme = Registry.CurrentUser.OpenSubKey(Personalize);
        bool light = (theme?.GetValue("AppsUseLightTheme") as int? ?? 1) != 0;
        bool hibernate = IsPwrHibernateAllowed();
        var dndState = await dnd.ConfigureAwait(false);
        return new(hibernate, binAvailable ? bin.Items : null, bin.Bytes, light, await mute.ConfigureAwait(false),
            dndState.IsAvailable ? dndState.IsOn : null);
    }

    public async Task RunAsync(SystemCommand command)
    {
        switch (command)
        {
            case SystemCommand.Lock: Check(LockWorkStation()); break;
            case SystemCommand.Sleep:
            case SystemCommand.Hibernate:
                // The managed caller stays responsive; no forced/critical suspension.
                await Task.Run(() => Suspend(command == SystemCommand.Hibernate));
                break;
            case SystemCommand.SignOut: await ShutdownAsync("/l"); break;
            case SystemCommand.Restart: await ShutdownAsync("/r", "/t", "0"); break;
            case SystemCommand.ShutDown: await ShutdownAsync("/s", "/t", "0"); break;
            case SystemCommand.EmptyRecycleBin:
                await Task.Run(() => Marshal.ThrowExceptionForHR(SHEmptyRecycleBinW(0, null, 1))); // SHERB_NOCONFIRMATION
                break;
            case SystemCommand.Dark:
            case SystemCommand.Light:
                await Task.Run(() => SetTheme(command == SystemCommand.Light)); break;
            case SystemCommand.Mute:
            case SystemCommand.Unmute:
                if (services.AudioOutput is not AudioOutputService audio || !await audio.SetMuteAsync(command == SystemCommand.Mute))
                    throw new InvalidOperationException("Sound is unavailable.");
                break;
            case SystemCommand.DndOn:
            case SystemCommand.DndOff:
                if (!await services.DoNotDisturb.SetAsync(command == SystemCommand.DndOn))
                    throw new InvalidOperationException("Do not disturb is unavailable.");
                break;
            case SystemCommand.ShowDesktop:
                object shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application", true)!)!;
                try { ((dynamic)shell).MinimizeAll(); }
                finally { Marshal.ReleaseComObject(shell); }
                break;
            case SystemCommand.TaskManager: await PaletteShellIndex.LaunchAsync(Path.Combine(Environment.SystemDirectory, "Taskmgr.exe")); break;
            case SystemCommand.Snipping: await PaletteShellIndex.LaunchAsync("ms-screenclip:"); break;
        }
    }

    private static async Task ShutdownAsync(params string[] args)
    {
        // No /f, and zero timeout: a non-zero timeout would implicitly force apps closed.
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "shutdown.exe")) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Windows power command unavailable.");
        await process.WaitForExitAsync();
        if (process.ExitCode != 0) throw new Win32Exception(process.ExitCode);
    }

    private static void SetTheme(bool light)
    {
        using var key = Registry.CurrentUser.CreateSubKey(Personalize);
        key.SetValue("AppsUseLightTheme", light ? 1 : 0, RegistryValueKind.DWord);
        key.SetValue("SystemUsesLightTheme", light ? 1 : 0, RegistryValueKind.DWord);
        SendMessageTimeoutW(0xffff, 0x001a, 0, "ImmersiveColorSet", 2, 100, out _);
    }

    private static void Suspend(bool hibernate)
    {
        // Enable only the privilege already granted to this token, for this call only.
        Check(OpenProcessToken(GetCurrentProcess(), 0x28, out var token)); // TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY
        using (token)
        {
            Check(LookupPrivilegeValueW(null, "SeShutdownPrivilege", out var luid));
            var enable = new TokenPrivileges { Count = 1, Id = luid, Attributes = 2 };
            bool adjusted = AdjustTokenPrivileges(token, false, in enable, Marshal.SizeOf<TokenPrivileges>(), out var prior, out _);
            int error = Marshal.GetLastPInvokeError(); // includes ERROR_NOT_ALL_ASSIGNED on a successful return
            if (!adjusted || error != 0) throw new Win32Exception(error);
            try { Check(SetSuspendState(hibernate, false, false)); }
            finally { if (prior.Count != 0) RestoreTokenPrivileges(token, false, in prior, 0, 0, 0); }
        }
    }

    private static void Check(bool result) { if (!result) throw new Win32Exception(Marshal.GetLastPInvokeError()); }
    [StructLayout(LayoutKind.Sequential)] private struct RecycleBinInfo { public int Size; public long Bytes, Items; }
    [StructLayout(LayoutKind.Sequential)] private struct Luid { public uint Low; public int High; }
    [StructLayout(LayoutKind.Sequential)] private struct TokenPrivileges { public uint Count; public Luid Id; public uint Attributes; }
    [DllImport("kernel32.dll")] private static extern nint GetCurrentProcess();
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(nint process, uint access, out SafeAccessTokenHandle token);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)] private static extern bool LookupPrivilegeValueW(string? system, string name, out Luid id);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool AdjustTokenPrivileges(SafeAccessTokenHandle token, bool disableAll, in TokenPrivileges state, int length, out TokenPrivileges previous, out int returned);
    [DllImport("advapi32.dll", EntryPoint = "AdjustTokenPrivileges", SetLastError = true)] private static extern bool RestoreTokenPrivileges(SafeAccessTokenHandle token, bool disableAll, in TokenPrivileges state, int length, nint previous, nint returned);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern int SHQueryRecycleBinW(string? root, ref RecycleBinInfo info);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern int SHEmptyRecycleBinW(nint owner, string? root, uint flags);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool LockWorkStation();
    [DllImport("powrprof.dll")] [return: MarshalAs(UnmanagedType.U1)] private static extern bool IsPwrHibernateAllowed();
    [DllImport("powrprof.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.U1)] private static extern bool SetSuspendState(
        [MarshalAs(UnmanagedType.U1)] bool hibernate, [MarshalAs(UnmanagedType.U1)] bool force, [MarshalAs(UnmanagedType.U1)] bool disableWake);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern nint SendMessageTimeoutW(
        nint hwnd, uint message, nint wParam, string text, uint flags, uint timeout, out nint result);
}
