using System.Text.RegularExpressions;

namespace FlowDesk.Core.Palette;

public sealed record ClipboardPrivacy(bool Excluded = false, uint? IncludeHistory = null, uint? UploadCloud = null, bool ReadFailed = false)
{
    public bool AllowsCapture(bool selfWrite) => !selfWrite && !ReadFailed && !Excluded && IncludeHistory != 0 && UploadCloud != 0;
}
public sealed record ClipboardEntry(long Id, string Text, DateTimeOffset CapturedAt)
{
    public string Display { get; } = Regex.Replace(Text.Trim(), @"\s+", " ");
    public string Subtitle(DateTimeOffset now)
    {
        var age = now - CapturedAt;
        string time = age.TotalMinutes < 1 ? "Just now" : age.TotalHours < 1 ? $"{(int)age.TotalMinutes} min ago" : age.TotalDays < 1 ? $"{(int)age.TotalHours} hr ago" : $"{(int)age.TotalDays} days ago";
        return $"{time} · {Text.Length} chars";
    }
}

/// <summary>UI-thread owned, memory only. Never serialize entries or use their contents as command IDs.</summary>
public sealed class ClipboardHistory
{
    public const int MaxItems = 50, MaxBytes = 100 * 1024;
    private readonly List<ClipboardEntry> items = [];
    private long nextId;
    public bool Enabled { get; private set; }
    public void SetEnabled(bool enabled) { Enabled = enabled; if (!enabled) Clear(); }
    public void Clear() => items.Clear();
    public void Delete(long id) => items.RemoveAll(e => e.Id == id);
    public ClipboardEntry[] Snapshot() => items.ToArray();
    public bool Capture(string? text, ClipboardPrivacy privacy, bool selfWrite, DateTimeOffset now)
    {
        if (!Enabled || !privacy.AllowsCapture(selfWrite) || string.IsNullOrWhiteSpace(text) || (long)text.Length * 2 > MaxBytes) return false;
        items.RemoveAll(i => string.Equals(i.Text, text, StringComparison.Ordinal));
        items.Insert(0, new(++nextId, text, now));
        if (items.Count > MaxItems) items.RemoveAt(items.Count - 1);
        return true;
    }
    public static bool Matches(ClipboardEntry entry, string query) => string.IsNullOrWhiteSpace(query) || CommandMatcher.Match(entry.Display, query, allowFuzzy: false).Score > 0;
}

public enum PaletteMode { Commands, Clipboard, Windows, Calculator, Web, Help, Files, Run }
public static class ClipboardModeRules
{
    public static PaletteMode Resolve(PaletteMode requested, bool enabled) => enabled ? requested : PaletteMode.Commands;
    public static PaletteMode Backspace(PaletteMode current, string query) => current == PaletteMode.Clipboard && query.Length == 0 ? PaletteMode.Commands : current;
}
public static class ClipboardPasteRules
{
    public static bool CanPaste(uint capturedProcess, uint currentProcess, uint ownProcess, bool exists, bool elevatedOrUnknown, bool foreground, bool modifiersDown) =>
        exists && capturedProcess != 0 && capturedProcess != ownProcess && capturedProcess == currentProcess && !elevatedOrUnknown && foreground && !modifiersDown;
}
