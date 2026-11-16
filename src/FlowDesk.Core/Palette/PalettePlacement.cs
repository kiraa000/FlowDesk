namespace FlowDesk.Core.Palette;

public sealed record PaletteRect(int X, int Y, int Width, int Height);
public static class PalettePlacement
{
    public static PaletteRect Place(PaletteRect work, double scale, double height = 520)
    {
        if (!double.IsFinite(scale) || scale <= 0) scale = 1;
        int width = Math.Min(work.Width, (int)Math.Round(640 * scale));
        int h = Math.Min(work.Height, (int)Math.Round(height * scale));
        return new(work.X + (work.Width - width) / 2,
            work.Y + Math.Clamp((int)Math.Round(work.Height * .27), 0, Math.Max(0, work.Height - h)), width, h);
    }
}
