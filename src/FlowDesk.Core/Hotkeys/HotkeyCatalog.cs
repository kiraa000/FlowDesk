namespace FlowDesk.Core.Hotkeys;

/// <summary>
/// One global shortcut. Modifiers are the Win32 MOD_* bits without MOD_NOREPEAT; the host adds
/// MOD_NOREPEAT when <see cref="Repeat"/> is false. VirtualKey is 0 for mouse-wheel bindings.
/// </summary>
public sealed record HotkeyBinding(
    string Id,
    uint Modifiers,
    uint VirtualKey,
    bool Repeat,
    bool Pausable,
    string Shortcut,
    string Label,
    WindowDirection? Direction = null,
    int DesktopNumber = 0,
    bool IsMouseWheel = false)
{
    /// <summary>True for Alt+1..5 and Alt+Shift+1..5 (registered only when desktops are available).</summary>
    public bool IsDesktop => DesktopNumber > 0;
}

/// <summary>
/// The complete binding table from the task brief. Desktop bindings come first, then snap, focus,
/// the single-key window commands, the terminal, reload and pause commands, and finally the wheel
/// pseudo-bindings.
/// </summary>
public static class HotkeyCatalog
{
    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const string PaletteId = "command-palette";
    public const string ClipboardId = "clipboard-history";
    public const string GameModeToggleId = "game-mode-toggle";
    public const uint ModShift = 0x0004;
    public const uint ModWin = 0x0008;
    public const string PauseToggleId = "pause-toggle";

    public static readonly IReadOnlyList<HotkeyBinding> All = Build();

    /// <summary>Bindings that use RegisterHotKey (everything except the low-level wheel hook).</summary>
    public static IEnumerable<HotkeyBinding> Registerable => All.Where(binding => !binding.IsMouseWheel);

    public static HotkeyBinding? Find(string id) => All.FirstOrDefault(binding => binding.Id == id);

    public static bool TryGetFocusDirection(HotkeyBinding binding, out WindowDirection direction) =>
        TryGetDirection(binding.Id, "focus-", out direction);

    public static bool TryGetSnapDirection(HotkeyBinding binding, out WindowDirection direction) =>
        TryGetDirection(binding.Id, "snap-", out direction);

    private static bool TryGetDirection(string id, string prefix, out WindowDirection direction)
    {
        direction = default;
        if (!id.StartsWith(prefix, StringComparison.Ordinal)) return false;
        string suffix = id[prefix.Length..];
        int dash = suffix.IndexOf('-');
        if (dash >= 0) suffix = suffix[..dash];
        return Enum.TryParse(suffix, ignoreCase: true, out direction);
    }

    private static IReadOnlyList<HotkeyBinding> Build()
    {
        var bindings = new List<HotkeyBinding>(32);
        // PowerToys Command Palette also defaults to Win+Alt+Space. RegisterHotKey reports the collision
        // as a normal conflict (error 1409), and the status text surfaces it; no special-casing is needed.
        bindings.Add(new(PaletteId, ModWin | ModAlt, 0x20, false, false, "Win+Alt+Space", "Open command palette"));
        bindings.Add(new(ClipboardId, ModWin | ModAlt, 0x56, false, false, "Win+Alt+V", "Open clipboard history"));
        bindings.Add(new(GameModeToggleId, ModControl | ModAlt, 0x47, false, false, "Ctrl+Alt+G", "Turn game mode on or off")); // no Win key: gaming keyboards often lock it
        for (int n = 1; n <= 5; n++)
        {
            bindings.Add(new($"desktop-switch-{n}", ModAlt, (uint)('0' + n), Repeat: false, Pausable: false,
                $"Alt+{n}", $"Go to desktop {n}", DesktopNumber: n));
        }
        for (int n = 1; n <= 5; n++)
        {
            bindings.Add(new($"desktop-move-{n}", ModAlt | ModShift, (uint)('0' + n), Repeat: false, Pausable: false,
                $"Alt+Shift+{n}", $"Move the active window to desktop {n} and follow it", DesktopNumber: n));
        }

        AddSnap(bindings, "snap-up", 0x57, WindowDirection.Up, "Alt+W", "Snap the active window up; repeated presses cycle 1/2, 1/3, 2/3 of the height");
        AddSnap(bindings, "snap-left", 0x41, WindowDirection.Left, "Alt+A", "Snap the active window left; repeated presses cycle 1/2, 1/3, 2/3 of the width");
        AddSnap(bindings, "snap-down", 0x53, WindowDirection.Down, "Alt+S", "Snap the active window down; repeated presses cycle 1/2, 1/3, 2/3 of the height");
        AddSnap(bindings, "snap-right", 0x44, WindowDirection.Right, "Alt+D", "Snap the active window right; repeated presses cycle 1/2, 1/3, 2/3 of the width");
        AddSnap(bindings, "snap-up-arrow", 0x26, WindowDirection.Up, "Alt+Up", "Snap the active window up; repeated presses cycle 1/2, 1/3, 2/3 of the height");
        AddSnap(bindings, "snap-left-arrow", 0x25, WindowDirection.Left, "Alt+Left", "Snap the active window left; repeated presses cycle 1/2, 1/3, 2/3 of the width");
        AddSnap(bindings, "snap-down-arrow", 0x28, WindowDirection.Down, "Alt+Down", "Snap the active window down; repeated presses cycle 1/2, 1/3, 2/3 of the height");
        AddSnap(bindings, "snap-right-arrow", 0x27, WindowDirection.Right, "Alt+Right", "Snap the active window right; repeated presses cycle 1/2, 1/3, 2/3 of the width");

        AddFocus(bindings, "focus-up", 0x57, WindowDirection.Up, "Alt+Shift+W", "Focus the window above");
        AddFocus(bindings, "focus-left", 0x41, WindowDirection.Left, "Alt+Shift+A", "Focus the window to the left");
        AddFocus(bindings, "focus-down", 0x53, WindowDirection.Down, "Alt+Shift+S", "Focus the window below");
        AddFocus(bindings, "focus-right", 0x44, WindowDirection.Right, "Alt+Shift+D", "Focus the window to the right");
        AddFocus(bindings, "focus-up-arrow", 0x26, WindowDirection.Up, "Alt+Shift+Up", "Focus the window above");
        AddFocus(bindings, "focus-left-arrow", 0x25, WindowDirection.Left, "Alt+Shift+Left", "Focus the window to the left");
        AddFocus(bindings, "focus-down-arrow", 0x28, WindowDirection.Down, "Alt+Shift+Down", "Focus the window below");
        AddFocus(bindings, "focus-right-arrow", 0x27, WindowDirection.Right, "Alt+Shift+Right", "Focus the window to the right");

        AddEdgeResize(bindings, "edge-resize-left", 0x25, WindowDirection.Left, "Alt+Ctrl+Left", "Move the active window's border left (touching windows resize with it)");
        AddEdgeResize(bindings, "edge-resize-right", 0x27, WindowDirection.Right, "Alt+Ctrl+Right", "Move the active window's border right (touching windows resize with it)");
        AddEdgeResize(bindings, "edge-resize-up", 0x26, WindowDirection.Up, "Alt+Ctrl+Up", "Move the active window's border up (touching windows resize with it)");
        AddEdgeResize(bindings, "edge-resize-down", 0x28, WindowDirection.Down, "Alt+Ctrl+Down", "Move the active window's border down (touching windows resize with it)");

        bindings.Add(new("maximize-toggle", ModAlt, 0x46, Repeat: false, Pausable: true,
            "Alt+F", "Maximize or restore the active window"));
        bindings.Add(new("minimize", ModAlt, 0x4D, Repeat: false, Pausable: true,
            "Alt+M", "Minimize the active window"));
        bindings.Add(new("next-window", ModAlt, 0x51, Repeat: false, Pausable: true,
            "Alt+Q", "Minimize the active window and focus the next one"));
        bindings.Add(new("center", ModAlt | ModShift, 0x20, Repeat: false, Pausable: true,
            "Alt+Shift+Space", "Center the active window"));
        bindings.Add(new("terminal-open", ModAlt, 0x0D, Repeat: false, Pausable: false,
            "Alt+Enter", "Open a terminal in your profile folder"));
        bindings.Add(new("engine-reload", ModAlt | ModShift, 0x52, Repeat: false, Pausable: false,
            "Alt+Shift+R", "Reload the shortcut engine and re-check the desktop library"));
        bindings.Add(new("pause", ModAlt | ModShift, 0x45, Repeat: false, Pausable: false,
            "Alt+Shift+E", "Pause window shortcuts; Alt+Shift+0 resumes"));
        bindings.Add(new(PauseToggleId, ModAlt | ModShift, 0x30, Repeat: false, Pausable: false,
            "Alt+Shift+0", "Pause or resume window shortcuts"));
        bindings.Add(new("resize-wheel", ModAlt | ModShift, 0, Repeat: true, Pausable: false,
            "Alt+Shift+Wheel", "Resize the active window", IsMouseWheel: true));
        bindings.Add(new("desktop-wheel", ModWin, 0, Repeat: true, Pausable: false,
            "Win+Wheel", "Switch desktop with the mouse wheel (up previous, down next)", IsMouseWheel: true));
        bindings.Add(new("desktop-move-wheel", ModWin | ModShift, 0, Repeat: true, Pausable: false,
            "Win+Shift+Wheel", "Move the active window to the previous/next desktop with the mouse wheel and follow it",
            IsMouseWheel: true));
        bindings.Add(new("search-wheel", ModWin | ModAlt, 0, Repeat: true, Pausable: true,
            "Win+Alt+Wheel", "Search open windows with the mouse wheel (up next, down undo)", IsMouseWheel: true));
        return bindings;
    }

    private static void AddSnap(
        List<HotkeyBinding> bindings, string id, uint virtualKey, WindowDirection direction, string shortcut, string label) =>
        bindings.Add(new(id, ModAlt, virtualKey, Repeat: true, Pausable: true, shortcut, label, Direction: direction));

    private static void AddFocus(
        List<HotkeyBinding> bindings, string id, uint virtualKey, WindowDirection direction, string shortcut, string label) =>
        bindings.Add(new(id, ModAlt | ModShift, virtualKey, Repeat: true, Pausable: true, shortcut, label, Direction: direction));

    private static void AddEdgeResize(
        List<HotkeyBinding> bindings, string id, uint virtualKey, WindowDirection direction, string shortcut, string label) =>
        bindings.Add(new(id, ModAlt | ModControl, virtualKey, Repeat: true, Pausable: true, shortcut, label, Direction: direction));
}
