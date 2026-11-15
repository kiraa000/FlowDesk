using System.Runtime.InteropServices;
using System.Text;
using FlowDesk.Core.Hotkeys;
using FlowDesk.Core.QuickLook;

namespace FlowDesk.App.Services;

internal static class QuickLookNative
{
    public static QuickLookContext Context(IQuickLookInput input, ShortcutModifiers modifiers)
    {
        nint foreground = GetForegroundWindow();
        string top = ClassName(foreground);
        if (top is not ("CabinetWClass" or "Progman" or "WorkerW"))
            return new(input.Enabled, foreground, top, "", false, false, modifiers, input.PreviewOwner);
        uint thread = GetWindowThreadProcessId(foreground, out _);
        var gui = new GuiInfo { Size = (uint)Marshal.SizeOf<GuiInfo>() };
        if (!GetGUIThreadInfo(thread, ref gui)) return default;
        string focus = ClassName(gui.Focus);
        bool shell = false;
        // Bounded ancestry walk, no cross-process messages, UI Automation or COM in the hook.
        for (nint parent = gui.Focus, i = 0; parent != 0 && i < 16; parent = GetParent(parent), i++)
        {
            if (ClassName(parent) == "SHELLDLL_DefView") { shell = true; break; }
            if (parent == foreground) break;
        }
        // Foreign-process IMM contexts cannot be inspected reliably (modern Explorer uses TSF).
        // Fail open for IME layouts rather than send synchronous messages from a global hook or
        // steal a composition Space. This conservatively also skips a closed East Asian IME.
        nint layout = GetKeyboardLayout(thread);
        int language = (int)((long)layout & 0x3ff);
        bool composing = ImmIsIME(layout) || language is 4 or 17 or 18;
        return new(input.Enabled, foreground, top, focus, shell && gui.Flags == 0, composing, modifiers, input.PreviewOwner);
    }
    public static string ClassName(nint hwnd)
    {
        var buffer = new StringBuilder(128);
        return GetClassNameW(hwnd, buffer, buffer.Capacity) > 0 ? buffer.ToString() : "";
    }
    [StructLayout(LayoutKind.Sequential)] private struct GuiInfo
    {
        public uint Size, Flags;
        public nint Active, Focus, Capture, MenuOwner, MoveSize, Caret;
        public int Left, Top, Right, Bottom;
    }
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint process);
    [DllImport("user32.dll")] private static extern bool GetGUIThreadInfo(uint thread, ref GuiInfo info);
    [DllImport("user32.dll")] private static extern nint GetParent(nint hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassNameW(nint hwnd, StringBuilder value, int length);
    [DllImport("user32.dll")] private static extern nint GetKeyboardLayout(uint thread);
    [DllImport("imm32.dll")] private static extern bool ImmIsIME(nint layout);
}
