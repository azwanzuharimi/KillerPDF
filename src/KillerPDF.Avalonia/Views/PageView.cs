using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace KillerPDF.Avalonia.Views;

public sealed class PageView : Control
{
    private WriteableBitmap? _bitmap;
    private string? _error;

    public PageView(int pageIndex) => PageIndex = pageIndex;

    public int PageIndex { get; }
    public double RenderedScale { get; private set; }
    public bool HasImage => _bitmap is not null || _error is not null;

    public void SetImage(WriteableBitmap bitmap, double scale)
    {
        WriteableBitmap? old = _bitmap;
        _bitmap = bitmap;
        _error = null;
        RenderedScale = scale;
        InvalidateVisual();
        old?.Dispose();
    }

    public void SetError(string message, double scale)
    {
        Clear();
        _error = message;
        RenderedScale = scale;
        InvalidateVisual();
    }

    public void Clear()
    {
        WriteableBitmap? old = _bitmap;
        _bitmap = null;
        _error = null;
        RenderedScale = 0;
        InvalidateVisual();
        old?.Dispose();
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        context.FillRectangle(Brushes.White, bounds);
        if (_bitmap is not null)
        {
            context.DrawImage(_bitmap, new Rect(_bitmap.Size), bounds);
        }
        else if (_error is not null)
        {
            var text = new FormattedText(_error, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                Typeface.Default, 12, Brushes.DarkRed) { MaxTextWidth = Math.Max(1, bounds.Width - 16) };
            context.DrawText(text, new Point(8, 8));
        }
    }
}
