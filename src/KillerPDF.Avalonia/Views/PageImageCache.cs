using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using KillerPDF.Avalonia.Core;

namespace KillerPDF.Avalonia.Views;

public sealed class PageImageCache(ScrollViewer viewer, Control content, Func<int, double> layoutScale)
{
    private const int Margin = 2;
    private IReadOnlyList<PageView> _views = [];
    private PageRasterizer? _rasterizer;
    private CancellationTokenSource? _cts;
    private (int First, int Last) _range = (-1, -1);

    public int FirstVisible { get; private set; }

    public void Reset(PageRasterizer? rasterizer, IReadOnlyList<PageView> views)
    {
        Cancel();
        foreach (PageView view in _views) view.Clear();
        _rasterizer = rasterizer;
        _views = views;
        _range = (-1, -1);
    }

    public void Update(bool force = false)
    {
        if (_rasterizer is null || _views.Count == 0) return;
        (int first, int last) = VisibleRange();
        FirstVisible = first;
        int from = Math.Max(0, first - Margin);
        int to = Math.Min(_views.Count - 1, last + Margin);
        if (!force && (from, to) == _range) return;
        _range = (from, to);
        Cancel();

        double screen = TopLevel.GetTopLevel(viewer)?.RenderScaling ?? 1;
        var jobs = new List<(PageView View, double Scale)>();
        for (int i = 0; i < _views.Count; i++)
        {
            PageView view = _views[i];
            if (i < from || i > to)
            {
                if (view.HasImage) view.Clear();
                continue;
            }
            double scale = layoutScale(i) * screen;
            if (view.RenderedScale != scale) jobs.Add((view, scale));
        }
        jobs.Sort((a, b) => Distance(a.View.PageIndex, first, last).CompareTo(Distance(b.View.PageIndex, first, last)));
        if (jobs.Count == 0) return;

        var cts = new CancellationTokenSource();
        _cts = cts;
        PageRasterizer rasterizer = _rasterizer;
        _ = Task.Run(() => RenderAll(rasterizer, jobs, cts.Token));
    }

    private static void RenderAll(PageRasterizer rasterizer, List<(PageView View, double Scale)> jobs, CancellationToken token)
    {
        foreach ((PageView view, double scale) in jobs)
        {
            RasterPage? page = null;
            string? error = null;
            try
            {
                lock (rasterizer) page = rasterizer.Render(view.PageIndex, scale, token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }
            Dispatcher.UIThread.Post(() =>
            {
                if (token.IsCancellationRequested) return;
                if (page is null) view.SetError(error ?? "Render failed.", scale);
                else view.SetImage(ToBitmap(page), scale);
            });
        }
    }

    private static WriteableBitmap ToBitmap(RasterPage page)
    {
        var bitmap = new WriteableBitmap(new PixelSize(page.Width, page.Height), new Vector(96, 96),
            PixelFormat.Bgra8888, AlphaFormat.Premul);
        using ILockedFramebuffer buffer = bitmap.Lock();
        int row = page.Width * 4;
        for (int y = 0; y < page.Height; y++)
            Marshal.Copy(page.Bgra, y * row, buffer.Address + y * buffer.RowBytes, row);
        return bitmap;
    }

    private (int First, int Last) VisibleRange()
    {
        double top = viewer.Offset.Y - content.Margin.Top;
        double bottom = top + viewer.Viewport.Height;
        int first = -1, last = -1;
        for (int i = 0; i < _views.Count; i++)
        {
            Point? p = _views[i].TranslatePoint(default, content);
            if (p is null) continue;
            if (p.Value.Y > bottom) break;
            if (p.Value.Y + _views[i].Bounds.Height <= top) continue;
            if (first < 0) first = i;
            last = i;
        }
        return first < 0 ? (0, 0) : (first, last);
    }

    private static int Distance(int i, int first, int last) => i < first ? first - i : i > last ? i - last : 0;

    private void Cancel()
    {
        _cts?.Cancel();
        _cts = null;
    }
}
