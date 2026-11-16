namespace FlowDesk.App.Services;

/// <summary>The presenter leaves WS_DLGFRAME behind even when its border is disabled.</summary>
internal static class PaletteWindowStyle
{
    public const uint Popup = 0x80000000;
    public const uint NativeFrame = 0x00C00000 | 0x00040000 | 0x00080000; // CAPTION (BORDER|DLGFRAME), THICKFRAME, SYSMENU

    public static uint Apply(uint style) => (style & ~NativeFrame) | Popup;
}
