using System.Globalization;

namespace FlowDesk.Core.Hotkeys;

/// <summary>Layout-independent Win32 virtual key plus MOD_* bits. Text is a persistence format,
/// not localized key labels. OEM keys use explicit VK hex names to avoid keyboard-layout ambiguity.</summary>
public readonly record struct KeyChord(uint Modifiers, uint VirtualKey)
{
    private static readonly IReadOnlyDictionary<uint, string> Names = new Dictionary<uint, string>
    {
        [0x08] = "Backspace", [0x09] = "Tab", [0x0D] = "Enter", [0x13] = "Pause",
        [0x14] = "CapsLock", [0x1B] = "Esc", [0x20] = "Space", [0x21] = "PageUp",
        [0x22] = "PageDown", [0x23] = "End", [0x24] = "Home", [0x25] = "Left",
        [0x26] = "Up", [0x27] = "Right", [0x28] = "Down", [0x2C] = "PrintScreen",
        [0x2D] = "Insert", [0x2E] = "Delete", [0x6A] = "Multiply", [0x6B] = "Add",
        [0x6D] = "Subtract", [0x6E] = "Decimal", [0x6F] = "Divide", [0x90] = "NumLock", [0x91] = "ScrollLock",
        [0xBE] = "."
    };
    public static KeyChord From(HotkeyBinding binding) => new(binding.Modifiers, binding.VirtualKey);
    public static bool IsModifier(uint key) => key is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or >= 0xA0 and <= 0xA5;
    public static uint ModifierFor(uint key) => key switch
    {
        0x10 or 0xA0 or 0xA1 => HotkeyCatalog.ModShift,
        0x11 or 0xA2 or 0xA3 => HotkeyCatalog.ModControl,
        0x12 or 0xA4 or 0xA5 => HotkeyCatalog.ModAlt,
        0x5B or 0x5C => HotkeyCatalog.ModWin, _ => 0
    };
    public string? ValidationError => (Modifiers & ~15u) != 0 || !SupportedKey(VirtualKey)
        ? "Choose a keyboard key that Windows can register."
        : Modifiers == 0 && VirtualKey is not (>= 0x7C and <= 0x87)
            ? "Use at least one modifier (Ctrl, Alt, Shift or Win) and one key, or F13–F24 alone." : null;
    private static bool SupportedKey(uint key) => !IsModifier(key) && (Names.ContainsKey(key) ||
        key is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A or >= 0x60 and <= 0x69 or >= 0x70 and <= 0x87 or
        >= 0xBA and <= 0xC0 or >= 0xDB and <= 0xDF or 0xE2);
    public override string ToString()
    {
        var parts = new List<string>();
        if ((Modifiers & 8) != 0) parts.Add("Win");
        if ((Modifiers & 2) != 0) parts.Add("Ctrl");
        if ((Modifiers & 1) != 0) parts.Add("Alt");
        if ((Modifiers & 4) != 0) parts.Add("Shift");
        if (VirtualKey != 0) parts.Add(KeyName(VirtualKey));
        return string.Join('+', parts);
    }
    private static string KeyName(uint key) => Names.TryGetValue(key, out var name) ? name :
        key is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A ? ((char)key).ToString() :
        key is >= 0x70 and <= 0x87 ? "F" + (key - 0x6F).ToString(CultureInfo.InvariantCulture) :
        key is >= 0x60 and <= 0x69 ? "Num" + (key - 0x60).ToString(CultureInfo.InvariantCulture) :
        "VK" + key.ToString("X2", CultureInfo.InvariantCulture);
    public static bool TryParse(string? text, out KeyChord chord)
    {
        chord = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        uint mods = 0, key = 0;
        foreach (var raw in text.Split('+'))
        {
            string part = raw.Trim();
            uint mod = part.ToUpperInvariant() switch { "WIN" => 8, "CTRL" => 2, "ALT" => 1, "SHIFT" => 4, _ => 0 };
            if (mod != 0) { if ((mods & mod) != 0) return false; mods |= mod; continue; }
            if (key != 0 || part.Length == 0) return false;
            var named = Names.FirstOrDefault(p => p.Value.Equals(part, StringComparison.OrdinalIgnoreCase));
            if (named.Key != 0) key = named.Key;
            else if (part.Length == 1 && char.IsAsciiLetterOrDigit(part[0])) key = char.ToUpperInvariant(part[0]);
            else if (part.StartsWith("F", StringComparison.OrdinalIgnoreCase) && uint.TryParse(part.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out uint f) && f is >= 1 and <= 24) key = 0x6F + f;
            else if (part.StartsWith("Num", StringComparison.OrdinalIgnoreCase) && uint.TryParse(part.AsSpan(3), NumberStyles.None, CultureInfo.InvariantCulture, out uint n) && n <= 9) key = 0x60 + n;
            else if (part.StartsWith("VK", StringComparison.OrdinalIgnoreCase) && uint.TryParse(part.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint vk)) key = vk;
            else return false;
        }
        chord = new(mods, key);
        return key != 0 && SupportedKey(key);
    }
}
