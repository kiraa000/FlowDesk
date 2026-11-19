namespace FlowDesk.App.Services;

internal static class PdfPreviewLimits
{
    internal const int MaxPages = 1000;
    internal static int PageCount(uint count) => (int)Math.Min(count, (uint)MaxPages);
}
