namespace FlowDesk.Core.QuickLook;

public static class ShellThumbnailPixels
{
    /// <summary>Shell's 32bpp DIB is premultiplied BGRA. Normalize row order and unpremultiply for
    /// PNG/Format32bppArgb. All-zero alpha is the legacy GDI convention for an opaque bitmap.</summary>
    public static byte[] StraightBgra(ReadOnlySpan<byte> source, int width, int height, int stride, bool topDown)
    {
        if (width <= 0 || height <= 0 || stride < checked(width * 4) || source.Length < checked(stride * height))
            throw new ArgumentException("Invalid Shell bitmap dimensions.");
        bool hasAlpha = false;
        for (int row = 0; row < height && !hasAlpha; row++)
            for (int col = 0; col < width; col++) if (source[row * stride + col * 4 + 3] != 0) { hasAlpha = true; break; }
        byte[] result = new byte[checked(width * height * 4)];
        for (int row = 0; row < height; row++)
        {
            int sourceRow = (topDown ? row : height - row - 1) * stride;
            for (int col = 0; col < width; col++)
            {
                int from = sourceRow + col * 4, to = (row * width + col) * 4;
                int alpha = hasAlpha ? source[from + 3] : 255;
                result[to + 3] = (byte)alpha;
                for (int channel = 0; channel < 3; channel++)
                    result[to + channel] = alpha == 0 ? (byte)0 : (byte)Math.Min(255, (source[from + channel] * 255 + alpha / 2) / alpha);
            }
        }
        return result;
    }
}
