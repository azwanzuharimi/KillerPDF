using KillerPdf.Engine.Documents;
using KillerPdf.Engine.Rendering;

namespace KillerPDF.Avalonia.Core;

public sealed record RasterPage(int Width, int Height, byte[] Bgra, IReadOnlyList<string> Diagnostics);

public sealed class PageRasterizer(PdfDocument document)
{
    private readonly PdfPageRenderer _renderer = new(document, MacFontResolver.Instance);
    private readonly IReadOnlyList<PdfPageInformation> _pages = PdfPageInformation.Read(document);

    public static (int Width, int Height) PixelSize(PdfPageInformation page, double scale)
    {
        int w = Math.Max(1, (int)Math.Round(page.Width * scale));
        int h = Math.Max(1, (int)Math.Round(page.Height * scale));
        return page.Rotation is 90 or 270 ? (h, w) : (w, h);
    }

    public static double ClampScale(PdfPageInformation page, double scale)
    {
        double bySide = (PdfRenderOptions.MaximumDimension - 1) / Math.Max(page.Width, page.Height);
        double byBytes = 0.99 * Math.Sqrt(PdfRenderOptions.MaximumPixelBytes / 4.0 / (page.Width * page.Height));
        return Math.Min(scale, Math.Min(bySide, byBytes));
    }

    public RasterPage Render(int pageIndex, double scale, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        (int w, int h) = PixelSize(_pages[pageIndex], ClampScale(_pages[pageIndex], scale));
        var options = new PdfRenderOptions(w, h) { CacheResult = false };
        byte[] pixels = new byte[w * h * 4];
        IReadOnlyList<string> diagnostics = _renderer.RenderInto(pageIndex, options, pixels, token);
        return new RasterPage(w, h, pixels, diagnostics);
    }
}
