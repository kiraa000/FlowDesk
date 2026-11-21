using System.Runtime.InteropServices;
using System.Text;
using FlowDesk.Core.HotCorners;
using FlowDesk.Core.Hotkeys;

namespace FlowDesk.App.Services;

internal sealed record HotCornerEnvironment(CornerPoint Point, CornerBounds[] Monitors, nint Foreground, string? Blocked);
internal static class HotCornerNative
{
    // QUNS_RUNNING_D3D_FULL_SCREEN only: busy/presentation also match ordinary video players.
    public static bool FullScreenGameRunning() => SHQueryUserNotificationState(out int state) >= 0 && state == 3;
    public static HotCornerEnvironment Sample(HotCornerMouseSnapshot mouse)
    {
        var monitors = new List<CornerBounds>();
        bool enumerated = EnumDisplayMonitors(0, 0, (nint monitor, nint dc, ref Rect rectangle, nint data) =>
        { monitors.Add(new(rectangle.Left, rectangle.Top, rectangle.Right, rectangle.Bottom)); return monitors.Count < 64; }, 0);
        bool cursor = GetCursorPos(out var point);
        nint foreground = GetForegroundWindow();
        Rect frame = default;
        bool window = foreground != 0 && GetWindowRect(foreground, out frame);
        if (foreground != 0 && DwmGetWindowAttribute(foreground, 9, out Rect extended, Marshal.SizeOf<Rect>()) == 0) frame = extended;
        var name = new StringBuilder(128); bool knownClass = GetClassNameW(foreground, name, name.Capacity) > 0;
        bool shell = name.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd";
        bool buttons = new[] { 1, 2, 4, 5, 6 }.Any(Down) || mouse.Buttons != 0;
        bool modifiers = new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C }.Any(Down);
        int hr = SHQueryUserNotificationState(out int notification);
        const int WS_CAPTION = 0x00C00000;
        bool covers = HotCornerGeometry.IsFullScreen(
            HotCornerGeometry.CoversMonitor(new(frame.Left, frame.Top, frame.Right, frame.Bottom), monitors),
            foreground != 0 && IsZoomed(foreground),
            foreground != 0 && (GetWindowLongW(foreground, -16) & WS_CAPTION) == WS_CAPTION);
        bool consistent = mouse.HasPosition && HotCornerGeometry.HookMatchesCursor(mouse.Point, new CornerPoint(point.X, point.Y), monitors);
        string? blocked = HotCornerGate.BlockReason(enumerated && monitors.Count > 0 && cursor && window && knownClass &&
            frame.Right > frame.Left && frame.Bottom > frame.Top && hr >= 0 && consistent,
            DefaultInputDesktop(), mouse.SessionBlocked, notification, covers, shell, buttons, modifiers);
        return new(new(point.X, point.Y), monitors.ToArray(), foreground, blocked);
    }
    private static bool DefaultInputDesktop()
    {
        nint desktop = OpenInputDesktop(0, false, 1); // DESKTOP_READOBJECTS, no switching/access elevation
        if (desktop == 0) return false;
        try
        {
            var name = new StringBuilder(256);
            return GetUserObjectInformationW(desktop, 2, name, name.Capacity * 2, out _) && name.ToString().Equals("Default", StringComparison.OrdinalIgnoreCase);
        }
        finally { CloseDesktop(desktop); }
    }
    public static bool Execute(HotCornerAction action)
    {
        if (action == HotCornerAction.Lock) return LockWorkStation();
        var keys = HotCornerActionKeys.For(action);
        if (keys.Length == 0) return false;
        Input[] inputs = keys.Select(k => Key(k.VirtualKey, k.KeyUp)).ToArray();
        uint inserted = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        if (inserted != inputs.Length)
        {
            // A partial insertion must not leave our synthetic chord held. Tagged cleanup is also
            // ignored by the shortcut tracker, just like the normal batch.
            Input[] release = HotCornerActionKeys.ReleaseAfterFailure(action).Select(k => Key(k.VirtualKey, k.KeyUp)).ToArray();
            SendInput((uint)release.Length, release, Marshal.SizeOf<Input>());
            return false;
        }
        return true;
    }
    private static Input Key(ushort key, bool up) => new() { Type = 1, Data = new InputUnion { Keyboard = new KeyboardInput {
        VirtualKey = key, Flags = (up ? 2u : 0u) | (key == 0x5B ? 1u : 0u), ExtraInfo = ShortcutKeyboardState.InjectionTag } } };
    private static bool Down(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public InputUnion Data; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion
    {
        [FieldOffset(0)] public KeyboardInput Keyboard;
        [FieldOffset(0)] public MouseInput Mouse;
    }
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardInput { public ushort VirtualKey, Scan; public uint Flags, Time; public nuint ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput { public int X, Y; public uint Data, Flags, Time; public nuint ExtraInfo; }
    private delegate bool MonitorProc(nint monitor, nint dc, ref Rect rectangle, nint data);
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(nint dc, nint clip, MonitorProc callback, nint data);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out Rect rectangle);
    [DllImport("user32.dll")] private static extern bool IsZoomed(nint hwnd);
    [DllImport("user32.dll")] private static extern int GetWindowLongW(nint hwnd, int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassNameW(nint hwnd, StringBuilder name, int count);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("shell32.dll")] private static extern int SHQueryUserNotificationState(out int state);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint hwnd, uint attribute, out Rect value, int size);
    [DllImport("user32.dll")] private static extern nint OpenInputDesktop(uint flags, bool inherit, uint access);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetUserObjectInformationW(nint handle, int index, StringBuilder name, int bytes, out uint needed);
    [DllImport("user32.dll")] private static extern bool CloseDesktop(nint desktop);
    [DllImport("user32.dll")] private static extern bool LockWorkStation();
    [DllImport("user32.dll")] private static extern uint SendInput(uint count, Input[] inputs, int size);
}
