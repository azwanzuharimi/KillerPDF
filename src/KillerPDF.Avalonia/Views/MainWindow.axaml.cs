using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using KillerPDF.Avalonia.Core;

namespace KillerPDF.Avalonia.Views;

public partial class MainWindow : Window
{
    private const double ThumbWidth = 140;
    private readonly PageImageCache _pageCache;
    private PageImageCache? _thumbCache;
    private PageRasterizer? _rasterizer;
    private List<PageView> _pageViews = [];
    private List<PageView> _thumbViews = [];
    private bool _rebuilding;
    private double _zoom = 1;

    public MainWindow()
    {
        InitializeComponent();
        _pageCache = new PageImageCache(Viewer, Pages, _ => Zoom);
        Viewer.ScrollChanged += (_, _) => OnPagesScrolled();
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        Opened += (_, _) => AttachThumbViewer();
    }

    public PdfSession? Session { get; private set; }
    internal IReadOnlyList<PageView> PageViews => _pageViews;
    internal IReadOnlyList<PageView> ThumbViews => _thumbViews;

    public IReadOnlyList<int> SelectedPages =>
        [.. PageList.Selection.SelectedIndexes.Order()];

    public double Zoom
    {
        get => _zoom;
        set
        {
            double zoom = Math.Clamp(value, 0.25, 5.0);
            if (zoom == _zoom) return;
            int top = _pageCache.FirstVisible;
            _zoom = zoom;
            ZoomText.Text = $"{Math.Round(zoom * 100)}%";
            SizePageViews();
            Dispatcher.UIThread.Post(() =>
            {
                ScrollToPage(top);
                _pageCache.Update(force: true);
            }, DispatcherPriority.Background);
        }
    }

    public void OpenFile(string path)
    {
        PdfSession session;
        try
        {
            session = PdfSession.Open(path);
        }
        catch (Exception ex)
        {
            _ = ShowMessage("Cannot open file", $"{Path.GetFileName(path)}\n\n{ex.Message}");
            return;
        }
        if (Session is not null) Session.Changed -= OnSessionChanged;
        Session = session;
        Session.Changed += OnSessionChanged;
        Title = Path.GetFileName(session.FilePath);
        RefreshDocument();
    }

    public void RefreshDocument()
    {
        if (Session is null) return;
        _rebuilding = true;
        var rasterizer = _rasterizer = new PageRasterizer(Session.Document);
        _pageViews = [.. Enumerable.Range(0, Session.PageCount).Select(i => new PageView(i))];
        _thumbViews = [.. Enumerable.Range(0, Session.PageCount).Select(i => new PageView(i))];
        SizePageViews();
        for (int i = 0; i < _thumbViews.Count; i++)
        {
            (int w, int h) = PageRasterizer.PixelSize(Session.Pages[i], ThumbScale(i));
            _thumbViews[i].Width = w;
            _thumbViews[i].Height = h;
        }
        _pageCache.Reset(rasterizer, _pageViews);
        _thumbCache?.Reset(rasterizer, _thumbViews);
        Pages.Children.Clear();
        Pages.Children.AddRange(_pageViews);
        PageList.Items.Clear();
        foreach (PageView thumb in _thumbViews)
        {
            PageList.Items.Add(new ListBoxItem
            {
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Content = new StackPanel
                {
                    Spacing = 4,
                    Children = { thumb, new TextBlock { Text = $"{thumb.PageIndex + 1}", HorizontalAlignment = HorizontalAlignment.Center } },
                },
            });
        }
        _rebuilding = false;
        UpdateStatus();
        Dispatcher.UIThread.Post(() =>
        {
            _pageCache.Update(force: true);
            _thumbCache?.Update(force: true);
            UpdateStatus();
        }, DispatcherPriority.Background);
    }

    private void OnSessionChanged(object? sender, EventArgs e) => RefreshDocument();

    private double ThumbScale(int index) => ThumbWidth / PageRasterizer.PixelSize(Session!.Pages[index], 1).Width;

    private void SizePageViews()
    {
        if (Session is null) return;
        foreach (PageView view in _pageViews)
        {
            (int w, int h) = PageRasterizer.PixelSize(Session.Pages[view.PageIndex], Zoom);
            view.Width = w;
            view.Height = h;
        }
    }

    private void AttachThumbViewer()
    {
        ScrollViewer? viewer = PageList.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (viewer is null || PageList.ItemsPanelRoot is not { } panel) return;
        _thumbCache = new PageImageCache(viewer, panel, ThumbScale);
        viewer.ScrollChanged += (_, _) => _thumbCache.Update();
        if (_rasterizer is null) return;
        _thumbCache.Reset(_rasterizer, _thumbViews);
        Dispatcher.UIThread.Post(() => _thumbCache.Update(force: true), DispatcherPriority.Background);
    }

    private void OnPagesScrolled()
    {
        _pageCache.Update();
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        FileText.Text = Session is null ? "" : Path.GetFileName(Session.FilePath);
        PageText.Text = Session is null ? "" : $"page {_pageCache.FirstVisible + 1} / {Session.PageCount}";
        DirtyText.Text = Session?.IsDirty == true ? "•" : "";
    }

    private void ScrollToPage(int index)
    {
        if (index < 0 || index >= _pageViews.Count) return;
        double y = Pages.Margin.Top + _pageViews[index].Bounds.Y - 8;
        Viewer.Offset = new Vector(Viewer.Offset.X, Math.Max(0, y));
    }

    private void OnPageSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_rebuilding || e.AddedItems.Count == 0) return;
        ScrollToPage(PageList.Items.IndexOf(e.AddedItems[^1]));
    }

    private void FitWidth()
    {
        if (Session is null) return;
        double widest = Session.Pages.Max(p => PageRasterizer.PixelSize(p, 1).Width);
        Zoom = (Viewer.Viewport.Width - Pages.Margin.Left - Pages.Margin.Right) / widest;
    }

    private async void OnOpen(object? sender, RoutedEventArgs e)
    {
        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open PDF",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("PDF") { Patterns = ["*.pdf"] }],
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path) OpenFile(path);
    }

    private void OnZoomIn(object? sender, RoutedEventArgs e) => Zoom *= 1.25;
    private void OnZoomOut(object? sender, RoutedEventArgs e) => Zoom /= 1.25;
    private void OnFitWidth(object? sender, RoutedEventArgs e) => FitWidth();

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Meta)) return;
        switch (e.Key)
        {
            case Key.O: OnOpen(null, e); break;
            case Key.OemPlus or Key.Add: Zoom *= 1.25; break;
            case Key.OemMinus or Key.Subtract: Zoom /= 1.25; break;
            case Key.D0 or Key.NumPad0: FitWidth(); break;
            default: return;
        }
        e.Handled = true;
    }

    private async Task ShowMessage(string title, string message)
    {
        var ok = new Button { Content = "OK", HorizontalAlignment = HorizontalAlignment.Right, IsDefault = true };
        var dialog = new Window
        {
            Title = title,
            Width = 420,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(16),
                Spacing = 12,
                Children = { new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap }, ok },
            },
        };
        ok.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(this);
    }
}
