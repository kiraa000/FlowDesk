using System.Runtime.InteropServices;
using FlowDesk.Core.Palette;
using FlowDesk.App.Views;

namespace FlowDesk.App.Services;

internal static class ClipboardPaste
{
    public static async Task<bool> TryPasteAsync(nint target, uint process, nint palette)
    {
        // Never steal focus back after the user has selected a third window.
        nint foreground = CommandPaletteWindow.Foreground;
        if (foreground != 0 && foreground != palette && foreground != target) return false;
        if (!ClipboardPasteRules.CanPaste(process, CommandPaletteWindow.ProcessOf(target), (uint)Environment.ProcessId,
            IsWindow(target), ElevatedOrUnknown(process), true, false)) return false;
        if (!SetForegroundWindow(target) && CommandPaletteWindow.Foreground != target) return false;
        // Real keys may still be held from Enter/hotkey invocation. Do not synthesize releases of
        // the user's keys; wait briefly, and fall back to copy rather than sending Win/Alt+Ctrl+V.
        for (int i = 0; i < 15 && ModifiersDown(); i++) await Task.Delay(20);
        if (!ClipboardPasteRules.CanPaste(process, CommandPaletteWindow.ProcessOf(target), (uint)Environment.ProcessId,
            IsWindow(target), ElevatedOrUnknown(process), CommandPaletteWindow.Foreground == target, ModifiersDown())) return false;
        ClipboardInput[] keys = [ClipboardInput.Keyboard(0x11), ClipboardInput.Keyboard(0x56), ClipboardInput.Keyboard(0x56, true), ClipboardInput.Keyboard(0x11, true)];
        if (SendInput((uint)keys.Length, keys, Marshal.SizeOf<ClipboardInput>()) == keys.Length) return true;
        ClipboardInput[] release = [ClipboardInput.Keyboard(0x56, true), ClipboardInput.Keyboard(0x11, true)];
        SendInput((uint)release.Length, release, Marshal.SizeOf<ClipboardInput>());
        return false;
    }
    private static bool ModifiersDown() => new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C, 0x0D }.Any(k => (GetAsyncKeyState(k) & 0x8000) != 0);
    private static bool ElevatedOrUnknown(uint process)
    {
        nint handle = OpenProcess(0x1000, false, process); if (handle == 0) return true;
        try
        {
            if (!OpenProcessToken(handle, 8, out nint token)) return true;
            try { return !GetTokenInformation(token, 20, out int elevated, 4, out _) || elevated != 0; }
            finally { CloseHandle(token); }
        }
        finally { CloseHandle(handle); }
    }
    [DllImport("user32.dll")] private static extern uint SendInput(uint count, ClipboardInput[] input, int size);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("kernel32.dll")] private static extern nint OpenProcess(uint access, bool inherit, uint process);
    [DllImport("advapi32.dll")] private static extern bool OpenProcessToken(nint process, uint access, out nint token);
    [DllImport("advapi32.dll")] private static extern bool GetTokenInformation(nint token, int info, out int value, int size, out int needed);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(nint handle);
}
