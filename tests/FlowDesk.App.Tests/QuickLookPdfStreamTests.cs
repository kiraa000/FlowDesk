using System.Text;
using FlowDesk.App.Services;
using Windows.Data.Pdf;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Xunit.Abstractions;

namespace FlowDesk.App.Tests;

public sealed class QuickLookPdfStreamTests(ITestOutputHelper output)
{
    [Fact]
    public async Task PdfRendersToDecodableRewoundStreamWithoutAnyWindow()
    {
        using var input = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(input.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(CreatePdf()); await writer.StoreAsync();
        }
        input.Seek(0);
        var document = await PdfDocument.LoadFromStreamAsync(input);
        Assert.Equal(1u, document.PageCount);
        using var page = document.GetPage(0);
        using var legacy = new InMemoryRandomAccessStream();
        await page.RenderToStreamAsync(legacy, new PdfPageRenderOptions { DestinationWidth = 600, DestinationHeight = 400 });
        output.WriteLine($"Legacy PDF stream: Position={legacy.Position}, Size={legacy.Size}");
        try
        {
            var decoder = await BitmapDecoder.CreateAsync(legacy);
            output.WriteLine($"Legacy no-seek decoder: {decoder.PixelWidth}x{decoder.PixelHeight}");
        }
        catch (Exception ex) { output.WriteLine($"Legacy no-seek decoder failed: {ex.GetType().Name} 0x{ex.HResult:X8}"); }

        using var stream = await QuickLookPdfStream.RenderAsync(page, 600, 400, default);
        Assert.Equal(0ul, stream.Position); Assert.True(stream.Size > 0);
        var bitmap = await BitmapDecoder.CreateAsync(stream);
        // Windows may apply the process DPI to DestinationWidth/Height (750x500 at 125%).
        // Assert usable output and aspect ratio, not an incorrect exact physical-pixel assumption.
        Assert.InRange(bitmap.PixelWidth, 1u, 2048u); Assert.InRange(bitmap.PixelHeight, 1u, 2048u);
        Assert.Equal(bitmap.PixelWidth * 2, bitmap.PixelHeight * 3);
        var pixels = await bitmap.GetPixelDataAsync();
        Assert.NotEmpty(pixels.DetachPixelData());
        output.WriteLine($"Production PDF stream: decodable {bitmap.PixelWidth}x{bitmap.PixelHeight}");
    }
    private static byte[] CreatePdf()
    {
        const string content = "BT /F1 16 Tf 30 120 Td (Quick Look PDF test) Tj ET";
        string[] objects = ["<< /Type /Catalog /Pages 2 0 R >>", "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 200] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>", $"<< /Length {content.Length} >>\nstream\n{content}\nendstream"];
        var text = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (int i = 0; i < objects.Length; i++) { offsets.Add(text.Length); text.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n"); }
        int xref = text.Length;
        text.Append("xref\n0 6\n0000000000 65535 f \r\n");
        foreach (int offset in offsets) text.Append($"{offset:D10} 00000 n \r\n");
        text.Append($"trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(text.ToString());
    }
}
