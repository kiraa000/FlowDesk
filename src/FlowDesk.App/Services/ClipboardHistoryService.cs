using System.Runtime.InteropServices;
using FlowDesk.Core.Palette;

namespace FlowDesk.App.Services;

/// <summary>All clipboard reads, writes and notifications run synchronously on the palette UI thread.
/// A single OpenClipboard transaction reads privacy metadata and text from the same owner.</summary>
internal sealed class ClipboardHistoryService : IDisposable
{
    private readonly nint hwnd;
    private readonly SubclassProc callback;
    private readonly Action changed;
    private readonly uint exclude = RegisterClipboardFormatW("ExcludeClipboardContentFromMonitorProcessing");
    private readonly uint history = RegisterClipboardFormatW("CanIncludeInClipboardHistory");
    private readonly uint cloud = RegisterClipboardFormatW("CanUploadToCloudClipboard");
    private bool listening, writing;
    private uint ownSequence;
    public ClipboardHistory History { get; } = new();
    public ClipboardHistoryService(nint hwnd, Action changed) { this.hwnd = hwnd; this.changed = changed; callback = OnMessage; }
    public void SetEnabled(bool enabled)
    {
        if (enabled == listening) { History.SetEnabled(enabled); return; }
        if (enabled)
        {
            if (exclude == 0 || history == 0 || cloud == 0 || !SetWindowSubclass(hwnd, callback, 16016, 0)) throw new InvalidOperationException("Clipboard monitor is unavailable.");
            if (!AddClipboardFormatListener(hwnd)) { RemoveWindowSubclass(hwnd, callback, 16016); throw new InvalidOperationException("Clipboard monitor is unavailable."); }
            listening = true; History.SetEnabled(true);
        }
        else
        {
            History.SetEnabled(false); listening = false;
            RemoveClipboardFormatListener(hwnd); RemoveWindowSubclass(hwnd, callback, 16016);
            changed();
        }
    }
    public void Clear() { History.Clear(); changed(); }
    private nint OnMessage(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        if (message == 0x031D && listening && !writing && GetClipboardSequenceNumber() != ownSequence)
        {
            // Never let clipboard failures (or contents) escape into a native callback or a crash log.
            try { Capture(); } catch { }
        }
        return DefSubclassProc(window, message, wParam, lParam);
    }
    private void Capture()
    {
        if (!OpenClipboard(hwnd)) return; // busy owners are skipped, never read later under stale metadata
        bool added = false;
        try
        {
            GetWindowThreadProcessId(GetClipboardOwner(), out uint ownerProcess);
            if (ownerProcess == (uint)Environment.ProcessId) return; // includes WinUI's own Ctrl+C/Ctrl+X
            bool failed = false;
            uint? ReadFlag(uint format)
            {
                if (!IsClipboardFormatAvailable(format)) return null;
                nint handle = GetClipboardData(format);
                if (handle == 0 || GlobalSize(handle) < 4) { failed = true; return null; }
                nint pointer = GlobalLock(handle);
                if (pointer == 0) { failed = true; return null; }
                try { return unchecked((uint)Marshal.ReadInt32(pointer)); } finally { GlobalUnlock(handle); }
            }
            var privacy = new ClipboardPrivacy(IsClipboardFormatAvailable(exclude), ReadFlag(history), ReadFlag(cloud), failed);
            if (!privacy.AllowsCapture(writing) || !IsClipboardFormatAvailable(13)) return;
            nint text = GetClipboardData(13);
            // Delayed rendering of text may materialize privacy formats too. Recheck before use.
            privacy = new ClipboardPrivacy(IsClipboardFormatAvailable(exclude), ReadFlag(history), ReadFlag(cloud), failed);
            if (!privacy.AllowsCapture(writing)) return;
            nuint bytes = text == 0 ? 0 : GlobalSize(text);
            // Global allocations may be rounded up. Bound the scan rather than treating allocation
            // padding as text; an over-limit item never gets allocated as a managed string.
            if (bytes < 2) return;
            nint ptr = GlobalLock(text); if (ptr == 0) return;
            try
            {
                int length = 0, max = (int)Math.Min(bytes / 2, (nuint)(ClipboardHistory.MaxBytes / 2 + 1));
                while (length < max && Marshal.ReadInt16(ptr, length * 2) != 0) length++;
                if (length == max) return; // malformed, unterminated
                added = History.Capture(Marshal.PtrToStringUni(ptr, length), privacy, writing, DateTimeOffset.UtcNow);
            }
            finally { GlobalUnlock(text); }
        }
        finally { CloseClipboard(); }
        if (added) changed();
    }
    public void Copy(string text)
    {
        // Mark every write as excluded as well as suppressing its sequence: queued notifications must
        // not re-capture it, and Windows/cloud history should not persist our memory-only history.
        nint value = AllocateText(text), marker = GlobalAlloc(0x42, 4);
        if (marker == 0) { GlobalFree(value); throw new InvalidOperationException("Clipboard unavailable."); }
        nint p = GlobalLock(marker);
        if (p == 0) { GlobalFree(value); GlobalFree(marker); throw new InvalidOperationException("Clipboard unavailable."); }
        Marshal.WriteInt32(p, 1); GlobalUnlock(marker);
        writing = true;
        try
        {
            if (!OpenClipboard(hwnd)) throw new InvalidOperationException("Clipboard busy.");
            try
            {
                ClipboardReplacement.Write(CaptureBackup, EmptyOrThrow, () =>
                {
                // Set the exclusion before text; if it fails, do not expose text without its privacy flag.
                if (SetClipboardData(exclude, marker) == 0) throw new InvalidOperationException("Clipboard unavailable.");
                marker = 0;
                if (SetClipboardData(13, value) == 0) throw new InvalidOperationException("Clipboard unavailable.");
                value = 0;
                ownSequence = GetClipboardSequenceNumber();
                });
            }
            finally { CloseClipboard(); }
        }
        finally { writing = false; if (value != 0) GlobalFree(value); if (marker != 0) GlobalFree(marker); }
    }
    private static nint AllocateText(string text)
    {
        nint h = GlobalAlloc(0x42, (nuint)((text.Length + 1) * 2));
        if (h == 0) throw new InvalidOperationException("Clipboard unavailable.");
        nint p = GlobalLock(h);
        if (p == 0) { GlobalFree(h); throw new InvalidOperationException("Clipboard unavailable."); }
        try { Marshal.Copy((text + '\0').ToCharArray(), 0, p, text.Length + 1); } finally { GlobalUnlock(h); }
        return h;
    }
    public void CopyFile(string path)
    {
        // CF_HDROP: DROPFILES header followed by a double-NUL terminated UTF-16 file list.
        byte[] names = System.Text.Encoding.Unicode.GetBytes(path + "\0\0");
        nint value = GlobalAlloc(0x42, (nuint)(20 + names.Length));
        if (value == 0) throw new InvalidOperationException("Clipboard unavailable.");
        try
        {
            nint p = GlobalLock(value);
            if (p == 0) throw new InvalidOperationException("Clipboard unavailable.");
            try { Marshal.WriteInt32(p, 20); Marshal.WriteInt32(p, 16, 1); Marshal.Copy(names, 0, p + 20, names.Length); }
            finally { GlobalUnlock(value); }
            writing = true;
            try
            {
                if (!OpenClipboard(hwnd)) throw new InvalidOperationException("Clipboard busy.");
                try
                {
                    ClipboardReplacement.Write(CaptureBackup, EmptyOrThrow, () =>
                    {
                        if (SetClipboardData(15, value) == 0) throw new InvalidOperationException("Clipboard unavailable.");
                        value = 0; ownSequence = GetClipboardSequenceNumber();
                    });
                }
                finally { CloseClipboard(); }
            }
            finally { writing = false; }
        }
        finally { if (value != 0) GlobalFree(value); }
    }
    public void Dispose() { SetEnabled(false); History.Clear(); }
    private static void EmptyOrThrow()
    {
        if (!EmptyClipboard()) throw new InvalidOperationException("Clipboard unavailable.");
    }
    private static IClipboardBackup CaptureBackup()
    {
        var backup = new ClipboardBackup();
        try
        {
            long total = 0;
            for (uint format = EnumClipboardFormats(0); format != 0; format = EnumClipboardFormats(format))
            {
                // Owner-display/private formats have application-specific ownership rules. Standard
                // GDI formats are duplicated by OLE and freed with their matching native API.
                if (format is not (1 or 2 or 3 or 4 or 5 or 6 or 7 or 9 or 10 or 11 or 12 or 13 or 14 or 15 or 16 or 17 or 0x81 or 0x82 or 0x83 or 0x8E) && format < 0xC000)
                    throw new InvalidOperationException("The current clipboard cannot be safely backed up.");
                nint source = GetClipboardData(format);
                bool gdi = format is 2 or 3 or 9 or 14 or 0x82 or 0x83 or 0x8E;
                nuint size = source == 0 || gdi ? 0 : GlobalSize(source);
                if (source == 0 || (!gdi && size == 0) || size > 32 * 1024 * 1024 || (total += (long)size) > 32 * 1024 * 1024)
                    throw new InvalidOperationException("The current clipboard cannot be safely backed up.");
                nint copy = ClipboardBackupFormat.EnhancedMetafile(format)
                    ? CopyEnhMetaFileW(source, null)
                    : OleDuplicateData(source, ClipboardBackupFormat.OleFormat(format), 0);
                if (copy == 0) throw new InvalidOperationException("Clipboard unavailable.");
                backup.Handles.Add((format, copy));
            }
            if (Marshal.GetLastWin32Error() != 0) throw new InvalidOperationException("Clipboard enumeration failed.");
            return backup;
        }
        catch { backup.Dispose(); throw; }
    }
    private sealed class ClipboardBackup : IClipboardBackup
    {
        internal readonly List<(uint Format, nint Handle)> Handles = [];
        public void Restore()
        {
            EmptyOrThrow();
            for (int i = 0; i < Handles.Count; i++)
            {
                var entry = Handles[i];
                if (SetClipboardData(entry.Format, entry.Handle) == 0) throw new InvalidOperationException("Clipboard restore failed.");
                Handles[i] = (entry.Format, 0);
            }
        }
        public void Dispose()
        {
            foreach (var entry in Handles)
            {
                if (entry.Handle == 0) continue;
                if (entry.Format is 2 or 9 or 0x82) DeleteObject(entry.Handle);
                else if (entry.Format is 14 or 0x8E) DeleteEnhMetaFile(entry.Handle);
                else
                {
                    if (entry.Format is 3 or 0x83)
                    {
                        nint pointer = GlobalLock(entry.Handle);
                        if (pointer != 0)
                        {
                            // METAFILEPICT: three LONGs, then pointer-aligned HMETAFILE.
                            DeleteMetaFile(Marshal.ReadIntPtr(pointer, IntPtr.Size == 8 ? 16 : 12));
                            GlobalUnlock(entry.Handle);
                        }
                    }
                    GlobalFree(entry.Handle);
                }
            }
        }
    }
    private delegate nint SubclassProc(nint hwnd, uint msg, nuint wp, nint lp, nuint id, nuint data);
    [DllImport("comctl32.dll")] private static extern bool SetWindowSubclass(nint hwnd, SubclassProc proc, nuint id, nuint data);
    [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(nint hwnd, SubclassProc proc, nuint id);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint hwnd, uint msg, nuint wp, nint lp);
    [DllImport("user32.dll")] private static extern bool AddClipboardFormatListener(nint hwnd);
    [DllImport("user32.dll")] private static extern bool RemoveClipboardFormatListener(nint hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterClipboardFormatW(string name);
    [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll")] private static extern nint GetClipboardOwner();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint process);
    [DllImport("user32.dll")] private static extern bool OpenClipboard(nint hwnd);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    [DllImport("user32.dll")] private static extern bool EmptyClipboard();
    [DllImport("user32.dll")] private static extern bool IsClipboardFormatAvailable(uint format);
    [DllImport("user32.dll")] private static extern nint GetClipboardData(uint format);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint EnumClipboardFormats(uint format);
    [DllImport("user32.dll")] private static extern nint SetClipboardData(uint format, nint value);
    [DllImport("ole32.dll")] private static extern nint OleDuplicateData(nint source, uint format, uint flags);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint handle);
    [DllImport("gdi32.dll")] private static extern bool DeleteEnhMetaFile(nint handle);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] private static extern nint CopyEnhMetaFileW(nint handle, string? fileName);
    [DllImport("gdi32.dll")] private static extern bool DeleteMetaFile(nint handle);
    [DllImport("kernel32.dll")] private static extern nint GlobalAlloc(uint flags, nuint bytes);
    [DllImport("kernel32.dll")] private static extern nint GlobalFree(nint memory);
    [DllImport("kernel32.dll")] private static extern nint GlobalLock(nint memory);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(nint memory);
    [DllImport("kernel32.dll")] private static extern nuint GlobalSize(nint memory);
}
