namespace FlowDesk.Core.Palette;

public static class PaletteAnswerLayout
{
    public const double MaximumHeight = 564;
    public static double Height(double question, double body, double status, double search = 44, double footer = 32)
    {
        static double Size(double value) => double.IsFinite(value) ? Math.Max(0, value) : 0;
        // Root: 12 px padding, two 8 px gaps and 1 px stroke. Answer: 4 px padding,
        // question/body gap, plus one gap only when a state/error line is present.
        double chrome = Math.Max(44, Size(search)) + Math.Max(32, Size(footer)) + 42;
        double content = Size(question) + Size(body) + Size(status) + 8 + (status > 0 ? 16 : 8);
        return Math.Clamp(Math.Ceiling((chrome + content) / 4) * 4, 152, MaximumHeight);
    }
}
