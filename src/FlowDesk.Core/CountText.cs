using System.Globalization;

namespace FlowDesk.Core;

/// <summary>Formats a count and an English noun consistently in user-facing text.</summary>
public static class CountText
{
    public static string Format(long count, string singular, string? plural = null, string? numberFormat = null, string suffix = "") =>
        $"{count.ToString(numberFormat, CultureInfo.CurrentCulture)}{suffix} {(count == 1 ? singular : plural ?? singular + "s")}";
}
