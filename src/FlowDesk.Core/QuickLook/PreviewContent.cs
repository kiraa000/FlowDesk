using System.Text;

namespace FlowDesk.Core.QuickLook;

public enum PreviewKind { Fallback, Image, Pdf, Media, Text, Folder }
public static class PreviewContent
{
    public const int TextLimit = 1024 * 1024;
    public static (uint Width, uint Height) Fit(double width, double height, uint limit)
    {
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0 || limit == 0) return (1, 1);
        double scale = Math.Min(1, limit / Math.Max(width, height));
        return ((uint)Math.Clamp(Math.Floor(width * scale), 1, limit), (uint)Math.Clamp(Math.Floor(height * scale), 1, limit));
    }
    private static readonly HashSet<string> Images = new("jpg jpeg png gif bmp webp ico tif tiff heic avif".Split(' '));
    private static readonly HashSet<string> Media = new("mp4 m4v mov avi mkv wmv webm mp3 wav m4a aac flac ogg wma".Split(' '));
    private static readonly HashSet<string> Text = new("txt md json xml yaml yml log csv ini cs js ts tsx jsx py ps1 css html htm sql sh bat cmd c cpp h rs go toml config".Split(' '));
    public static PreviewKind Choose(string? path, bool folder, ReadOnlySpan<byte> prefix)
    {
        if (folder) return PreviewKind.Folder;
        string ext = Path.GetExtension(path ?? "").TrimStart('.').ToLowerInvariant();
        if (Images.Contains(ext)) return PreviewKind.Image;
        if (ext == "pdf") return PreviewKind.Pdf;
        if (Media.Contains(ext)) return PreviewKind.Media;
        if (Text.Contains(ext)) return PreviewKind.Text;
        if (ext is "doc" or "docx" or "xls" or "xlsx" or "ppt" or "pptx" or "odt" or "ods" or "odp") return PreviewKind.Fallback;
        if (prefix.StartsWith("%PDF-"u8)) return PreviewKind.Pdf;
        if (prefix.StartsWith(new byte[] { 137, 80, 78, 71 }) || prefix.StartsWith(new byte[] { 255, 216, 255 }) ||
            prefix.StartsWith("GIF8"u8) || prefix.StartsWith("BM"u8) || prefix.StartsWith("II*\0"u8) ||
            prefix.StartsWith("MM\0*"u8) || prefix.StartsWith(new byte[] { 0, 0, 1, 0 })) return PreviewKind.Image;
        if (prefix.Length >= 12 && prefix[..4].SequenceEqual("RIFF"u8))
            return prefix.Slice(8, 4).SequenceEqual("WEBP"u8) ? PreviewKind.Image :
                prefix.Slice(8, 4).SequenceEqual("WAVE"u8) || prefix.Slice(8, 4).SequenceEqual("AVI "u8) ? PreviewKind.Media : PreviewKind.Fallback;
        if (prefix.Length >= 12 && prefix.Slice(4, 4).SequenceEqual("ftyp"u8))
        {
            var brand = prefix.Slice(8, 4);
            return brand.SequenceEqual("avif"u8) || brand.SequenceEqual("avis"u8) || brand.SequenceEqual("heic"u8) ||
                brand.SequenceEqual("heix"u8) || brand.SequenceEqual("mif1"u8) ? PreviewKind.Image : PreviewKind.Media;
        }
        if (prefix.StartsWith("ID3"u8) || prefix.StartsWith("fLaC"u8) || prefix.StartsWith("OggS"u8)) return PreviewKind.Media;
        if (prefix.StartsWith(new byte[] { 255, 254 }) || prefix.StartsWith(new byte[] { 254, 255 }) ||
            prefix.StartsWith(new byte[] { 239, 187, 191 })) return PreviewKind.Text;
        if (prefix.Length > 0 && !prefix.Contains((byte)0) && prefix.AllText()) return PreviewKind.Text;
        return PreviewKind.Fallback;
    }
    private static bool AllText(this ReadOnlySpan<byte> bytes)
    {
        foreach (byte b in bytes) if (b < 32 && b is not (9 or 10 or 13)) return false;
        return true;
    }
    public static async Task<(string Text, bool Truncated)> ReadTextAsync(Stream stream, CancellationToken token)
    {
        byte[] bytes = new byte[TextLimit + 1];
        int count = 0, read;
        while (count < bytes.Length && (read = await stream.ReadAsync(bytes.AsMemory(count), token)) > 0) count += read;
        bool truncated = count > TextLimit;
        count = Math.Min(count, TextLimit);
        Encoding encoding = new UTF8Encoding(false, false);
        int skip = 0;
        if (count >= 4 && bytes.AsSpan(0, 4).SequenceEqual(new byte[] { 255, 254, 0, 0 })) { encoding = Encoding.UTF32; skip = 4; }
        else if (count >= 4 && bytes.AsSpan(0, 4).SequenceEqual(new byte[] { 0, 0, 254, 255 })) { encoding = new UTF32Encoding(true, true); skip = 4; }
        else if (count >= 2 && bytes[0] == 255 && bytes[1] == 254) { encoding = Encoding.Unicode; skip = 2; }
        else if (count >= 2 && bytes[0] == 254 && bytes[1] == 255) { encoding = Encoding.BigEndianUnicode; skip = 2; }
        else if (count >= 3 && bytes[0] == 239 && bytes[1] == 187 && bytes[2] == 191) skip = 3;
        else
        {
            int evenZeros = 0, oddZeros = 0, sample = Math.Min(count, 256);
            for (int i = 0; i < sample; i++) if (bytes[i] == 0) { if (i % 2 == 0) evenZeros++; else oddZeros++; }
            if (sample >= 4 && oddZeros > sample / 4 && evenZeros == 0) encoding = Encoding.Unicode;
            else if (sample >= 4 && evenZeros > sample / 4 && oddZeros == 0) encoding = Encoding.BigEndianUnicode;
            else
            {
                try
                {
                    // Validate without flushing when capped: a cut UTF-8 tail is not an ANSI signal.
                    var strict = new UTF8Encoding(false, true).GetDecoder();
                    strict.GetCharCount(bytes, 0, count, !truncated);
                }
                catch (DecoderFallbackException)
                {
                    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                    encoding = Encoding.GetEncoding(1252);
                }
            }
        }
        // Decoder.Convert avoids a replacement character for a multi-byte codepoint cut by the cap.
        char[] chars = new char[encoding.GetMaxCharCount(count)];
        encoding.GetDecoder().Convert(bytes, skip, count - skip, chars, 0, chars.Length, !truncated,
            out _, out int used, out _);
        return (new string(chars, 0, used), truncated);
    }
}

/// <summary>UI-thread generation guard; late uncancellable WinRT/COM results cannot publish.</summary>
public sealed class PreviewGeneration
{
    private long current;
    public long Next() => ++current;
    public bool IsCurrent(long generation) => current == generation;
}
