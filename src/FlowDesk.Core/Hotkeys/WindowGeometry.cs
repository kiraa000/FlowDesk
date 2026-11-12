using FlowDesk.Core.Model;

namespace FlowDesk.Core.Hotkeys;

/// <summary>
/// Snap, center and wheel-resize rectangles. Pure maths only: the caller supplies the monitor
/// work area and the window's current outer rect in physical pixels.
/// </summary>
public static class WindowGeometry
{
    /// <summary>Overlap between the two halves; it is the AutoHotkey script's border compensation.</summary>
    public const int Gap = -8;

    /// <summary>Half of the work area, left/right/up/down, with the two halves overlapping by 8 px.</summary>
    public static PixelRect Snap(PixelRect area, WindowDirection direction)
    {
        int width = area.Width, height = area.Height;
        switch (direction)
        {
            case WindowDirection.Left:
            {
                int half = Half(width);
                return new PixelRect(area.X, area.Y, half, height);
            }
            case WindowDirection.Right:
            {
                int half = Half(width);
                int x = area.X + half + Gap;
                return new PixelRect(x, area.Y, area.Right - x, height);
            }
            case WindowDirection.Up:
            {
                int half = Half(height);
                return new PixelRect(area.X, area.Y, width, half);
            }
            case WindowDirection.Down:
            {
                int half = Half(height);
                int y = area.Y + half + Gap;
                return new PixelRect(area.X, y, width, area.Bottom - y);
            }
            default:
                return new PixelRect(area.X, area.Y, width, height);
        }
    }

    /// <summary>Keeps the window size and centres it in the work area, rounding halves away from zero.</summary>
    public static PixelRect Center(PixelRect area, PixelRect window)
    {
        int x = Round(area.X + (area.Width - window.Width) / 2.0);
        int y = Round(area.Y + (area.Height - window.Height) / 2.0);
        return new PixelRect(x, y, window.Width, window.Height);
    }

    /// <summary>
    /// Grows or shrinks around the window centre by 8 %, clamps to 300x200 and then to the work area
    /// (keeping the window fully inside it).
    /// </summary>
    public static PixelRect Resize(PixelRect area, PixelRect window, bool grow)
    {
        double factor = grow ? 1.08 : 0.92;
        double cx = window.X + window.Width / 2.0;
        double cy = window.Y + window.Height / 2.0;

        int newWidth = Round(window.Width * factor);
        int newHeight = Round(window.Height * factor);
        newWidth = Math.Max(newWidth, 300);
        newHeight = Math.Max(newHeight, 200);
        newWidth = Math.Min(newWidth, area.Width);
        newHeight = Math.Min(newHeight, area.Height);

        int x = Round(cx - newWidth / 2.0);
        int y = Round(cy - newHeight / 2.0);
        if (x < area.X) x = area.X;
        if (y < area.Y) y = area.Y;
        if (x + newWidth > area.Right) x = area.Right - newWidth;
        if (y + newHeight > area.Bottom) y = area.Bottom - newHeight;
        return new PixelRect(x, y, newWidth, newHeight);
    }

    private static int Half(int extent) => (int)Math.Floor((extent - Gap) / 2.0);

    private static int Round(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);
}
