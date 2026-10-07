using KillerPdf.Engine.Documents;
using KillerPDF.Avalonia.Core;

namespace KillerPDF.Avalonia.Tests;

public class PageRasterizerTests
{
    [Fact]
    public void PixelSizeSwapsForQuarterTurns()
    {
        var page = new PdfPageInformation { Width = 200, Height = 100, Rotation = 90 };
        Assert.Equal((200, 400), PageRasterizer.PixelSize(page, 2));
        Assert.Equal((400, 200), PageRasterizer.PixelSize(page with { Rotation = 180 }, 2));
    }

    [Fact]
    public void RotatedPageRendersLandscapeBitmap()
    {
        string dir = TestPdf.TempDir();
        PdfSession s = PdfSession.Open(TestPdf.Create(Path.Combine(dir, "r.pdf"), 300));
        s.Rotate([0], 90);
        RasterPage page = new PageRasterizer(s.Document).Render(0, 1, CancellationToken.None);
        Assert.Equal((500, 300), (page.Width, page.Height));
        Assert.Equal(500 * 300 * 4, page.Bgra.Length);
    }

    [Fact]
    public void CancelledRenderThrows()
    {
        string dir = TestPdf.TempDir();
        PdfSession s = PdfSession.Open(TestPdf.Create(Path.Combine(dir, "c.pdf"), 300));
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => new PageRasterizer(s.Document).Render(0, 1, cts.Token));
    }
}
