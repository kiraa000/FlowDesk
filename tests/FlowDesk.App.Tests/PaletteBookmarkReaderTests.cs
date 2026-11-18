using System.Runtime.InteropServices;
using FlowDesk.App.Services;
using FlowDesk.Core.Palette;

namespace FlowDesk.App.Tests;

public sealed class PaletteBookmarkReaderTests
{
    [Fact] public void FirefoxSnapshotReadsWalWithoutOpeningOrLockingTheOriginalDatabase()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "bookmark-fixture-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        string live = Path.Combine(root, "places.sqlite");
        Assert.Equal(0, sqlite3_open_v2(live, out nint database, 6, 0));
        try
        {
            const string sql = "PRAGMA journal_mode=WAL; CREATE TABLE moz_places(id INTEGER, url TEXT); CREATE TABLE moz_bookmarks(guid TEXT,title TEXT,fk INTEGER,type INTEGER); INSERT INTO moz_places VALUES(1,'https://example.com/docs'); INSERT INTO moz_bookmarks VALUES('guid','Docs',1,1),('second','Docs',1,1),('third','Docs',1,1);";
            Assert.Equal(0, sqlite3_exec(database, sql, 0, 0, out nint error));
            if (error != 0) sqlite3_free(error);
            Assert.Equal(0, sqlite3_exec(database, "INSERT INTO moz_bookmarks VALUES('copy1','Docs duplicate',1,1),('copy2','Docs again',1,1);", 0, 0, out error));
            if (error != 0) sqlite3_free(error);
            Assert.True(File.Exists(live + "-wal"));
            byte[] original = ReadShared(live), wal = ReadShared(live + "-wal");
            string identity = PaletteBookmarkReader.SqliteIdentity(live);
            var bookmark = Assert.Single(PaletteBookmarkReader.ReadFirefoxSnapshot(live, new(BookmarkBrowser.Firefox, "Test", root, "firefox.exe")));
            Assert.Equal("Docs", bookmark.Title); Assert.Equal("https://example.com/docs", bookmark.Url);
            Assert.Equal(original, ReadShared(live)); Assert.Equal(wal, ReadShared(live + "-wal"));
            Assert.Equal(0, sqlite3_exec(database, "INSERT INTO moz_places VALUES(2,'https://example.com/after');", 0, 0, out error));
            if (error != 0) sqlite3_free(error);
            Assert.NotEqual(identity, PaletteBookmarkReader.SqliteIdentity(live));
        }
        finally { sqlite3_close(database); }
    }
    private static byte[] ReadShared(string path)
    {
        // The fixture writer is deliberately still open: the observer must share its write access.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var bytes = new MemoryStream(); stream.CopyTo(bytes); return bytes.ToArray();
    }
    [Fact] public void OlderSettingsEnableAllBookmarkBrowsers()
    {
        var settings = AppSettings.FromJson("{}"); Assert.True(settings.PaletteEdgeBookmarks); Assert.True(settings.PaletteChromeBookmarks); Assert.True(settings.PaletteFirefoxBookmarks);
    }
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_open_v2([MarshalAs(UnmanagedType.LPUTF8Str)] string path, out nint db, int flags, nint vfs);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_exec(nint db, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, nint callback, nint argument, out nint error);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern void sqlite3_free(nint value);
    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_close(nint db);
}
