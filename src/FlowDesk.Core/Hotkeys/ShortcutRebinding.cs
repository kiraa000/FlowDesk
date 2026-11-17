namespace FlowDesk.Core.Hotkeys;

public enum WindowsShortcutKind { Warning, Reserved }
public sealed record WindowsShortcut(KeyChord Chord, WindowsShortcutKind Kind, string Action);

/// <summary>Conservative Windows 11 table. Secure-attention/lock and shell task switching are blocked;
/// shell commands which RegisterHotKey can replace are warnings, not claims of availability.
/// F12 is reserved for the debugger (RegisterHotKey documentation). Runtime registration remains authoritative.</summary>
public static class WindowsShortcuts
{
    public static IReadOnlyList<WindowsShortcut> All { get; } = Build();
    private static IReadOnlyList<WindowsShortcut> Build()
    {
        var rows = new List<WindowsShortcut>();
        void Add(string keys, WindowsShortcutKind kind, string action) { KeyChord.TryParse(keys, out var chord); rows.Add(new(chord, kind, action)); }
        Add("Win+L", WindowsShortcutKind.Reserved, "Lock the PC");
        Add("Ctrl+Alt+Delete", WindowsShortcutKind.Reserved, "Windows security");
        Add("Alt+Tab", WindowsShortcutKind.Reserved, "Switch apps");
        Add("Alt+Shift+Tab", WindowsShortcutKind.Reserved, "Switch apps backwards");
        Add("Ctrl+Shift+Esc", WindowsShortcutKind.Reserved, "Task Manager");
        foreach (var (keys, action) in new[] { ("Win+D", "Show desktop"), ("Win+E", "File Explorer"), ("Win+R", "Run"),
            ("Win+I", "Settings"), ("Win+Tab", "Task view"), ("Win+V", "Clipboard history"), ("Win+.", "Emoji panel"),
            ("Win+Shift+S", "Screen snipping"), ("Alt+F4", "Close window"), ("Win+Space", "Input language"),
            ("Win+A", "Quick settings"), ("Win+N", "Notifications"), ("Win+S", "Search"), ("Win+X", "Quick link menu"),
            ("Win+P", "Project"), ("Win+K", "Cast"), ("Win+G", "Game Bar"), ("Win+H", "Voice typing"),
            ("Win+Ctrl+D", "New desktop"), ("Win+Ctrl+F4", "Close desktop"), ("Win+Ctrl+Left", "Previous desktop"), ("Win+Ctrl+Right", "Next desktop") })
            Add(keys, WindowsShortcutKind.Warning, action);
        for (int n = 0; n <= 9; n++) Add("Win+" + n, WindowsShortcutKind.Warning, "Open taskbar app");
        foreach (string key in new[] { "Up", "Down", "Left", "Right" })
        { Add("Win+" + key, WindowsShortcutKind.Warning, "Snap window"); Add("Win+Shift+" + key, WindowsShortcutKind.Warning, "Move window"); }
        for (uint modifiers = 0; modifiers <= 15; modifiers++) rows.Add(new(new(modifiers, 0x7B), WindowsShortcutKind.Reserved, "Debugger key F12"));
        return rows.AsReadOnly();
    }
    public static WindowsShortcut? Find(KeyChord chord) => All.FirstOrDefault(x => x.Chord == chord);
}

public sealed record ShortcutConflictResult(string? Error, WindowsShortcut? Windows, IReadOnlyList<HotkeyBinding> Others)
{
    public bool Blocked => Error is not null || Windows?.Kind == WindowsShortcutKind.Reserved;
}
public enum ShortcutResolution { None, TurnOtherOff, Swap }
public sealed record ShortcutEditPlan(Dictionary<string, string> Overrides, IReadOnlyList<string> DisabledIds);

public static class ShortcutRebinding
{
    public static Dictionary<string, string> Normalize(IReadOnlyDictionary<string, string>? overrides, Action<string>? report = null)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (id, text) in overrides ?? new Dictionary<string, string>())
        {
            var binding = HotkeyCatalog.Find(id);
            if (binding is null || binding.IsMouseWheel || !KeyChord.TryParse(text, out var chord) || chord.ValidationError is not null || WindowsShortcuts.Find(chord)?.Kind == WindowsShortcutKind.Reserved)
            { report?.Invoke("Ignored invalid shortcut override."); continue; }
            if (chord != KeyChord.From(binding)) result[id] = chord.ToString();
        }
        return result;
    }
    public static IReadOnlyList<HotkeyBinding> Effective(IReadOnlyDictionary<string, string>? overrides)
    {
        var normalized = Normalize(overrides);
        var table = HotkeyCatalog.All.Select(b => normalized.TryGetValue(b.Id, out var value) && KeyChord.TryParse(value, out var c)
            ? b with { Modifiers = c.Modifiers, VirtualKey = c.VirtualKey, Shortcut = c.ToString() } : b).ToArray();
        int pause = Array.FindIndex(table, b => b.Id == "pause");
        table[pause] = table[pause] with { Label = "Pause window shortcuts; " + table.Single(b => b.Id == HotkeyCatalog.PauseToggleId).Shortcut + " resumes" };
        return table;
    }
    public static ShortcutConflictResult Evaluate(IEnumerable<HotkeyBinding> effective, string id, KeyChord candidate, bool quickLook = false,
        bool hotCornerDesktop = false, bool hotCornerTaskView = false)
    {
        // Quick Look only handles unmodified Space; valid editable chords cannot collide with it.
        string? error = candidate.ValidationError;
        if (quickLook && candidate == new KeyChord(0, 0x20)) error = "Space is used by Quick Look in Explorer and on the desktop.";
        // These are fixed action outputs, not editable hotkey triggers. Registering them would
        // intercept Hot corners' synthetic shell command. Never silently break that module.
        if (hotCornerDesktop && candidate == new KeyChord(8, 0x44)) error = "Win+D is used by Hot corners to show the desktop. Choose other keys or turn Hot corners off in Settings first.";
        if (hotCornerTaskView && candidate == new KeyChord(8, 0x09)) error = "Win+Tab is used by Hot corners for Task View. Choose other keys or turn Hot corners off in Settings first.";
        return new(error, WindowsShortcuts.Find(candidate), effective.Where(b => !b.IsMouseWheel && b.Id != id && KeyChord.From(b) == candidate).ToArray());
    }
    public static ShortcutEditPlan Plan(IReadOnlyDictionary<string, string>? overrides, IEnumerable<string>? disabled,
        string id, KeyChord candidate, ShortcutResolution resolution)
    {
        var table = Effective(overrides);
        var current = table.Single(b => b.Id == id && !b.IsMouseWheel);
        var check = Evaluate(table, id, candidate);
        if (check.Blocked) throw new InvalidOperationException(check.Error ?? "This shortcut is reserved by Windows.");
        if (check.Others.Count > 0 && resolution == ShortcutResolution.None) throw new InvalidOperationException("Choose how to resolve the FlowDesk conflict.");
        if (resolution == ShortcutResolution.Swap && check.Others.Count != 1) throw new InvalidOperationException("Swap needs exactly one other shortcut.");
        var next = Normalize(overrides);
        var off = new HashSet<string>(new ShortcutPolicy(disabled).DisabledIds, StringComparer.Ordinal);
        next[id] = candidate.ToString();
        foreach (var other in check.Others)
        {
            if (resolution == ShortcutResolution.TurnOtherOff) off.Add(other.Id);
            else if (resolution == ShortcutResolution.Swap) next[other.Id] = KeyChord.From(current).ToString();
        }
        var nextTable = Effective(next);
        if (nextTable.Where(b => !b.IsMouseWheel && !off.Contains(b.Id)).GroupBy(KeyChord.From).Any(g => g.Count() > 1))
            throw new InvalidOperationException("This change would leave two enabled FlowDesk shortcuts with the same keys. Turn the other shortcut off first.");
        return new(Normalize(next), off.ToArray());
    }
}
