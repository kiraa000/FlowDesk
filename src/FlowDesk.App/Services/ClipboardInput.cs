using System.Runtime.InteropServices;
using FlowDesk.Core.Hotkeys;

namespace FlowDesk.App.Services;

// x64 INPUT: the union begins at 8 and reserves 32 bytes for MOUSEINPUT, even for keyboard input.
[StructLayout(LayoutKind.Explicit, Size = 40)]
internal struct ClipboardInput
{
    [FieldOffset(0)] public uint Type;
    [FieldOffset(8)] public ushort Key;
    [FieldOffset(12)] public uint Flags;
    [FieldOffset(24)] public nuint ExtraInfo;
    public static ClipboardInput Keyboard(ushort key, bool up = false) => new()
    { Type = 1, Key = key, Flags = up ? 2u : 0u, ExtraInfo = ShortcutKeyboardState.InjectionTag };
}
