using Windows.Data.Pdf;
using Windows.Storage.Streams;

namespace FlowDesk.App.Services;

/// <summary>Returns a readable PNG stream at its beginning; caller owns it. No XAML/window needed.</summary>
internal static class QuickLookPdfStream
{
    public static async Task<InMemoryRandomAccessStream> RenderAsync(PdfPage page, uint width, uint height, CancellationToken token)
    {
        var stream = new InMemoryRandomAccessStream();
        try
        {
            await page.RenderToStreamAsync(stream, new PdfPageRenderOptions { DestinationWidth = width, DestinationHeight = height }).AsTask(token);
            token.ThrowIfCancellationRequested();
            stream.Seek(0);
            return stream;
        }
        catch { stream.Dispose(); throw; }
    }
}
