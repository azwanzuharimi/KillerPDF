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
        DragDrop.AddDragOverHandler(this, OnDragOver);
        DragDrop.AddDropHandler(this, OnDrop);
        UpdateCommands();
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

    public void OpenFile(string path) => _ = OpenFileAsync(path);

    public async Task OpenFileAsync(string path)
    {
        if (!await ConfirmDiscard()) return;
        PdfSession session;
        try
        {
            session = PdfSession.Open(path);
        }
        catch (Exception ex)
        {
            await ShowMessage("Cannot open file", $"{Path.GetFileName(path)}\n\n{ex.Message}");
            return;
        }
        if (Session is not null) Session.Changed -= OnSessionChanged;
        Session = session;
        Session.Changed += OnSessionChanged;
        RefreshDocument();
    }

    public void RefreshDocument()
    {
        if (Session is null) return;
        _rebuilding = true;
        IReadOnlyList<int> keep = PageList.ItemCount == Session.PageCount ? SelectedPages : [];
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
        foreach (int index in keep) PageList.Selection.Select(index);
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
        if (Session is not null) Title = Path.GetFileName(Session.FilePath);
        UpdateCommands();
    }

    private void ScrollToPage(int index)
    {
        if (index < 0 || index >= _pageViews.Count) return;
        double y = Pages.Margin.Top + _pageViews[index].Bounds.Y - 8;
        Viewer.Offset = new Vector(Viewer.Offset.X, Math.Max(0, y));
    }

    private void OnPageSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_rebuilding) return;
        UpdateCommands();
        if (e.AddedItems.Count == 0) return;
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
        if (e.Key is Key.Back or Key.Delete && e.KeyModifiers == KeyModifiers.None && PageList.IsKeyboardFocusWithin)
        {
            _ = DeletePages(SelectedPages);
            e.Handled = true;
            return;
        }
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Meta)) return;
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        switch (e.Key)
        {
            case Key.O: OnOpen(null, e); break;
            case Key.S when shift: OnSaveAs(); break;
            case Key.S: if (Session is not null) SaveDocument(); break;
            case Key.Z when shift: if (Session?.CanRedo == true) OnRedo(null, e); break;
            case Key.Z: if (Session?.CanUndo == true) OnUndo(null, e); break;
            case Key.OemPlus or Key.Add: Zoom *= 1.25; break;
            case Key.OemMinus or Key.Subtract: Zoom /= 1.25; break;
            case Key.D0 or Key.NumPad0: FitWidth(); break;
            default: return;
        }
        e.Handled = true;
    }
}
