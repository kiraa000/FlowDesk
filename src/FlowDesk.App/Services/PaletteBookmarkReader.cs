using System.Runtime.InteropServices;
using FlowDesk.Core.Palette;
using Microsoft.Win32;

namespace FlowDesk.App.Services;

/// <summary>Background snapshot reader; never opens the browser's live SQLite database. A failed or
/// changing file retains the last good snapshot. Cache keys include WAL timestamp and length.</summary>
internal sealed class PaletteBookmarkReader
{
    private readonly Dictionary<string, (string Stamp, IReadOnlyList<BookmarkEntry> Items)> cache = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<BookmarkEntry> Read(BookmarkOptions options)
    {
        var result = new List<BookmarkEntry>();
        foreach (var profile in Profiles(options))
        {
            string path = System.IO.Path.Combine(profile.Path, profile.Browser == BookmarkBrowser.Firefox ? "places.sqlite" : "Bookmarks");
            try
            {
                string stamp = profile.Browser == BookmarkBrowser.Firefox ? SqliteIdentity(path) : Stamp(path);
                if (cache.TryGetValue(path, out var saved) && saved.Stamp == stamp) { result.AddRange(saved.Items); continue; }
                if (!File.Exists(path)) continue;
                var items = profile.Browser == BookmarkBrowser.Firefox ? ReadFirefoxSnapshot(path, profile) : BookmarkParser.Chromium(ReadText(path), profile);
                string after = profile.Browser == BookmarkBrowser.Firefox ? SqliteIdentity(path) : Stamp(path);
                if (stamp != after) throw new IOException("Bookmark file changed while reading.");
                cache[path] = (stamp, items); result.AddRange(items);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or ExternalException or InvalidOperationException or DllNotFoundException or EntryPointNotFoundException)
            { if (cache.TryGetValue(path, out var saved)) result.AddRange(saved.Items); }
        }
        return result.DistinctBy(b => b.DedupeKey).ToArray();
    }
    private static string Stamp(string path)
    { var info = new FileInfo(path); return info.Exists ? $"{info.LastWriteTimeUtc.Ticks}:{info.Length};" : "missing;"; }
    internal static string SqliteIdentity(string path) => Fingerprint(path) + Fingerprint(path + "-wal");
    private static string Fingerprint(string path)
    {
        if (!File.Exists(path)) return "missing;";
        // Timestamps alone can lag while Firefox keeps handles open. Include database change
        // counters / WAL salts and its tail, detecting appends, resets and checkpoints at equal size.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        byte[] bytes = new byte[164];
        int header = stream.Read(bytes, 0, 100);
        stream.Position = Math.Max(0, stream.Length - 64);
        int tail = stream.Read(bytes, 100, 64);
        return Stamp(path) + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes.AsSpan(0, 100 + tail))) + $":{header};";
    }
    private static string ReadText(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length > 16 * 1024 * 1024) throw new IOException("Bookmark file is too large.");
        using var reader = new StreamReader(stream); return reader.ReadToEnd();
    }
    private static IEnumerable<BookmarkProfile> Profiles(BookmarkOptions options)
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        foreach (var browser in new[] { BookmarkBrowser.Edge, BookmarkBrowser.Chrome })
        {
            if (!options.Enabled(browser)) continue;
            string root = System.IO.Path.Combine(local, browser == BookmarkBrowser.Edge ? @"Microsoft\Edge\User Data" : @"Google\Chrome\User Data");
            string exe = Executable(browser == BookmarkBrowser.Edge ? "msedge.exe" : "chrome.exe");
            string[] folders;
            try { folders = Directory.Exists(root) ? Directory.GetDirectories(root) : []; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
            foreach (string folder in folders)
                if (File.Exists(System.IO.Path.Combine(folder, "Bookmarks"))) yield return new(browser, System.IO.Path.GetFileName(folder), folder, exe);
        }
        if (options.Firefox)
        {
            string root = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Mozilla", "Firefox");
            IReadOnlyList<BookmarkProfile> firefox;
            try { firefox = BookmarkParser.FirefoxProfiles(ReadText(System.IO.Path.Combine(root, "profiles.ini")), root, Executable("firefox.exe")); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { firefox = []; }
            foreach (var profile in firefox) yield return profile;
        }
    }
    private static string Executable(string name)
    {
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using var root = RegistryKey.OpenBaseKey(hive.Name == "HKEY_CURRENT_USER" ? RegistryHive.CurrentUser : RegistryHive.LocalMachine, view);
                    using var key = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\App Paths\" + name);
                    if (key?.GetValue("") is string path && File.Exists(path.Trim('"'))) return path.Trim('"');
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
            }
        return name; // ShellExecute also resolves registered App Paths and PATH.
    }
    /// <summary>Snapshots live under the app's own temp folder, never a fixed development path.</summary>
    internal static string SnapshotRoot() => System.IO.Path.Combine(System.IO.Path.GetTempPath(), "FlowDesk", "bookmarks");
    internal static IReadOnlyList<BookmarkEntry> ReadFirefoxSnapshot(string live, BookmarkProfile profile)
    {
        string root = SnapshotRoot();
        string directory = System.IO.Path.Combine(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory); string copy = System.IO.Path.Combine(directory, "places.sqlite");
        nint database = 0, statement = 0;
        try
        {
            string before = SqliteIdentity(live);
            CopyShared(live, copy);
            if (File.Exists(live + "-wal")) CopyShared(live + "-wal", copy + "-wal");
            if (before != SqliteIdentity(live)) throw new IOException("Bookmark database changed while copying. Try again on the next open.");
            if (sqlite3_open_v2(copy, out database, 1, 0) != 0) throw new InvalidOperationException("Bookmark snapshot is unavailable.");
            sqlite3_busy_timeout(database, 150);
            const string sql = "SELECT b.guid, b.title, p.url FROM moz_bookmarks b JOIN moz_places p ON b.fk=p.id WHERE b.type=1 LIMIT 10000";
            if (sqlite3_prepare_v2(database, sql, -1, out statement, 0) != 0) throw new InvalidOperationException("Bookmark snapshot is unavailable.");
            var result = new List<BookmarkEntry>(); int status;
            while ((status = sqlite3_step(statement)) == 100)
            {
                string Text(int col) => Marshal.PtrToStringUTF8(sqlite3_column_text(statement, col)) ?? "";
                if (BookmarkParser.Entry(Text(0), Text(1), Text(2), profile) is { } bookmark) result.Add(bookmark);
            }
            if (status != 101) throw new InvalidOperationException("Bookmark snapshot is unavailable.");
            return result.DistinctBy(b => b.DedupeKey).ToArray();
        }
        finally
        {
            if (statement != 0) sqlite3_finalize(statement);
            if (database != 0) sqlite3_close(database);
            // Only our GUID-named snapshot files, never the browser's originals.
            foreach (string path in new[] { copy, copy + "-wal", copy + "-shm" }) try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            try { Directory.Delete(directory); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
    private static void CopyShared(string source, string target)
    {
        using var read = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (read.Length > 512L * 1024 * 1024) throw new IOException("Bookmark database is too large.");
        using var write = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None); read.CopyTo(write);
    }
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_open_v2([MarshalAs(UnmanagedType.LPUTF8Str)] string path, out nint db, int flags, nint vfs);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_prepare_v2(nint db, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, int bytes, out nint statement, nint tail);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_step(nint statement);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern nint sqlite3_column_text(nint statement, int column);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_finalize(nint statement);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_close(nint db);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_busy_timeout(nint db, int milliseconds);
}
