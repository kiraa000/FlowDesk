namespace FlowDesk.Core.Hotkeys;

/// <summary>
/// One global shortcut. Modifiers are the Win32 MOD_* bits without MOD_NOREPEAT; the host adds
/// MOD_NOREPEAT when <see cref="Repeat"/> is false. VirtualKey is 0 for the mouse-wheel binding.
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
/// the single-key window commands, the pause toggle and finally the wheel pseudo-binding.
/// </summary>
public static class HotkeyCatalog
{
    public const uint ModAlt = 0x0001;
    public const uint ModShift = 0x0004;
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

        AddSnap(bindings, "snap-up", 0x57, WindowDirection.Up, "Alt+W", "Snap the active window to the top half");
        AddSnap(bindings, "snap-left", 0x41, WindowDirection.Left, "Alt+A", "Snap the active window to the left half");
        AddSnap(bindings, "snap-down", 0x53, WindowDirection.Down, "Alt+S", "Snap the active window to the bottom half");
        AddSnap(bindings, "snap-right", 0x44, WindowDirection.Right, "Alt+D", "Snap the active window to the right half");
        AddSnap(bindings, "snap-up-arrow", 0x26, WindowDirection.Up, "Alt+Up", "Snap the active window to the top half");
        AddSnap(bindings, "snap-left-arrow", 0x25, WindowDirection.Left, "Alt+Left", "Snap the active window to the left half");
        AddSnap(bindings, "snap-down-arrow", 0x28, WindowDirection.Down, "Alt+Down", "Snap the active window to the bottom half");
        AddSnap(bindings, "snap-right-arrow", 0x27, WindowDirection.Right, "Alt+Right", "Snap the active window to the right half");

        AddFocus(bindings, "focus-up", 0x57, WindowDirection.Up, "Alt+Shift+W", "Focus the window above");
        AddFocus(bindings, "focus-left", 0x41, WindowDirection.Left, "Alt+Shift+A", "Focus the window to the left");
        AddFocus(bindings, "focus-down", 0x53, WindowDirection.Down, "Alt+Shift+S", "Focus the window below");
        AddFocus(bindings, "focus-right", 0x44, WindowDirection.Right, "Alt+Shift+D", "Focus the window to the right");
        AddFocus(bindings, "focus-up-arrow", 0x26, WindowDirection.Up, "Alt+Shift+Up", "Focus the window above");
        AddFocus(bindings, "focus-left-arrow", 0x25, WindowDirection.Left, "Alt+Shift+Left", "Focus the window to the left");
        AddFocus(bindings, "focus-down-arrow", 0x28, WindowDirection.Down, "Alt+Shift+Down", "Focus the window below");
        AddFocus(bindings, "focus-right-arrow", 0x27, WindowDirection.Right, "Alt+Shift+Right", "Focus the window to the right");

        bindings.Add(new("maximize-toggle", ModAlt, 0x46, Repeat: false, Pausable: true,
            "Alt+F", "Maximize or restore the active window"));
        bindings.Add(new("minimize", ModAlt, 0x4D, Repeat: false, Pausable: true,
            "Alt+M", "Minimize the active window"));
        bindings.Add(new("next-window", ModAlt, 0x51, Repeat: false, Pausable: true,
            "Alt+Q", "Minimize the active window and focus the next one"));
        bindings.Add(new("center", ModAlt | ModShift, 0x20, Repeat: false, Pausable: true,
            "Alt+Shift+Space", "Center the active window"));
        bindings.Add(new(PauseToggleId, ModAlt | ModShift, 0x30, Repeat: false, Pausable: false,
            "Alt+Shift+0", "Pause or resume window shortcuts"));
        bindings.Add(new("resize-wheel", ModAlt | ModShift, 0, Repeat: true, Pausable: false,
            "Alt+Shift+Wheel", "Resize the active window", IsMouseWheel: true));
        return bindings;
    }

    private static void AddSnap(
        List<HotkeyBinding> bindings, string id, uint virtualKey, WindowDirection direction, string shortcut, string label) =>
        bindings.Add(new(id, ModAlt, virtualKey, Repeat: true, Pausable: true, shortcut, label, Direction: direction));

    private static void AddFocus(
        List<HotkeyBinding> bindings, string id, uint virtualKey, WindowDirection direction, string shortcut, string label) =>
        bindings.Add(new(id, ModAlt | ModShift, virtualKey, Repeat: true, Pausable: true, shortcut, label, Direction: direction));
}
