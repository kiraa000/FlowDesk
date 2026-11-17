using System.Runtime.InteropServices;
using FlowDesk.Core.Hotkeys;

namespace FlowDesk.App.Services;

/// <summary>Dialog-scoped capture. Armed only while the capture control owns focus and our window
/// is foreground. Navigation Tab/Esc is never swallowed. Never synthesizes keyboard input.</summary>
internal sealed class ShortcutCaptureHook : IDisposable
{
    private readonly HookProc callback;
    private readonly Action<uint, bool> observe;
    private readonly Action previousFocus;
    private readonly Action resetHeld;
    private readonly HashSet<uint> swallowed = [];
    private readonly nint window;
    private nint hook;
    public bool Armed { get; set; }
    public ShortcutCaptureHook(nint window, Action<uint, bool> observe, Action previousFocus, Action resetHeld)
    {
        this.window = window; this.observe = observe; this.previousFocus = previousFocus; this.resetHeld = resetHeld; callback = OnKey;
        hook = SetWindowsHookExW(13, callback, GetModuleHandleW(null), 0);
        if (hook == 0) throw new InvalidOperationException("Keyboard capture is unavailable. Your shortcut was not changed.");
    }
    private nint OnKey(int code, nint message, nint data)
    {
        if (code < 0) return CallNextHookEx(hook, code, message, data);
        try
        {
            uint key = unchecked((uint)Marshal.ReadInt32(data));
            bool down = message is 0x100 or 0x104;
            bool up = message is 0x101 or 0x105;
            if (!down && !up) return CallNextHookEx(hook, code, message, data);
            bool foreground = GetForegroundWindow() == window;
            if (!foreground) resetHeld();
            if (up && swallowed.Remove(key)) { observe(key, false); return 1; }
            bool navigation = key == 0x1B || (key == 0x09 && !swallowed.Any(k => (KeyChord.ModifierFor(k) & (1 | 2 | 8)) != 0));
            if (Armed && foreground && key == 0x09 && navigation && down && swallowed.Any(k => KeyChord.ModifierFor(k) == 4))
            { swallowed.Add(key); previousFocus(); return 1; }
            if (Armed && foreground && !navigation)
            {
                observe(key, down);
                if (down) swallowed.Add(key);
                return 1;
            }
        }
        catch (Exception) { /* fail open; never cross a hook boundary */ }
        return CallNextHookEx(hook, code, message, data);
    }
    public void Dispose() { Armed = false; if (hook != 0) { UnhookWindowsHookEx(hook); hook = 0; } }
    private delegate nint HookProc(int code, nint message, nint data);
    [DllImport("user32.dll")] private static extern nint SetWindowsHookExW(int id, HookProc proc, nint module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandleW(string? name);
}
