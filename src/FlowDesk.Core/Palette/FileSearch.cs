using System.Buffers.Binary;
using System.Text;

namespace FlowDesk.Core.Palette;

public sealed record PaletteFile(string Path, bool IsFolder)
{
    public string Id => "file:" + Path.ToUpperInvariant();
    public string Name => System.IO.Path.GetFileName(Path.TrimEnd('\\', '/'));
    public static PaletteFile FromWindowsSearch(string path, uint attributes) => new(path, (attributes & 0x10) != 0);
}

public interface IPaletteFileBackend
{
    string Name { get; }
    Task<IReadOnlyList<PaletteFile>> SearchAsync(string query, int limit, CancellationToken token);
}

public static class FileSearchQuery
{
    public static bool CanElevate(string path) => System.IO.Path.GetExtension(path).ToLowerInvariant() is ".exe" or ".bat" or ".cmd";
    // Quoting makes Everything's operators literal; double quotes themselves cannot be file-name characters.
    public static string Everything(string query) => "\"" + query.Replace("\"", " ").Replace('\0', ' ').Trim() + "\"";
    public static string WindowsSearch(string query, string profile, int limit)
    {
        string scope = new Uri(System.IO.Path.GetFullPath(profile).TrimEnd('\\') + "\\").AbsoluteUri;
        string literal = query.Replace("'", "''").Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]").Replace('\0', ' ');
        return $"SELECT TOP {Math.Clamp(limit, 1, 80)} System.ItemPathDisplay, System.FileAttributes FROM SystemIndex " +
            $"WHERE SCOPE = '{scope.Replace("'", "''")}' AND System.FileName LIKE '%{literal}%' ORDER BY System.DateModified DESC";
    }
    public static string MiddleEllipsis(string text, int max = 76) => text.Length <= max ? text : text[..(max / 2)] + "…" + text[^(max - max / 2 - 1)..];
}

/// <summary>Everything SDK's packed DWORD-based IPC v1 wire format (including on x64).
/// No SDK binary is required. Offsets are checked before decoding untrusted reply bytes.</summary>
public static class EverythingIpcWire
{
    public static byte[] Query(uint replyWindow, uint replyId, string query, int limit)
    {
        byte[] text = Encoding.Unicode.GetBytes(FileSearchQuery.Everything(query) + '\0');
        // sizeof(QUERYW) is 24 due to DWORD alignment; the string begins at byte 20.
        byte[] data = new byte[22 + text.Length];
        Write(0, replyWindow); Write(4, replyId); Write(8, 0); Write(12, 0); Write(16, (uint)Math.Clamp(limit, 1, 80));
        text.CopyTo(data, 20);
        return data;
        void Write(int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset), value);
    }
    public static IReadOnlyList<PaletteFile> Results(byte[] data, int limit)
    {
        if (data.Length < 28 || data.Length > 1024 * 1024) throw new FormatException("Invalid file search reply.");
        uint count = Read(20);
        if (count > 80 || 28L + count * 12L > data.Length) throw new FormatException("Invalid file search reply.");
        var result = new List<PaletteFile>();
        for (int i = 0; i < Math.Min(count, Math.Clamp(limit, 1, 80)); i++)
        {
            int at = 28 + i * 12;
            string name = Text(Read(at + 4)), folder = Text(Read(at + 8));
            if (name.IndexOfAny(['\\', '/', '\0']) >= 0 || name is "." or "..") continue;
            string path = System.IO.Path.Combine(folder, name);
            if (!System.IO.Path.IsPathFullyQualified(path)) continue;
            result.Add(new(path, (Read(at) & 1) != 0));
        }
        return result;
        uint Read(int at) => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at, 4));
        string Text(uint offset)
        {
            if ((offset & 1) != 0 || offset < 28L + count * 12L || offset >= data.Length - 1) throw new FormatException("Invalid file search reply.");
            int end = (int)offset;
            while (end + 1 < data.Length && (data[end] != 0 || data[end + 1] != 0)) end += 2;
            if (end + 1 >= data.Length) throw new FormatException("Invalid file search reply.");
            return Encoding.Unicode.GetString(data, (int)offset, end - (int)offset);
        }
    }
}
