using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using KillerPdf.Engine.Authoring;
using KillerPDF.Avalonia.Views;
using Xunit.Abstractions;

namespace KillerPDF.Avalonia.Tests;

public class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

public class MainWindowTests(ITestOutputHelper output)
{
    private static readonly string Sample = Path.Combine(RepoRoot(), "KillerPDF.pdf");
    internal static readonly HeadlessUnitTestSession Headless = HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder));
    private static string? Shots => Environment.GetEnvironmentVariable("KP_SHOTS");

    [Fact]
    public Task OpenShowsFirstPageAndThumbnails() => Headless.Dispatch(() =>
    {
        var window = Show();
        window.OpenFile(Sample);
        Assert.True(Pump(() => window.PageViews[0].HasImage && window.ThumbViews[0].HasImage));
        Assert.Equal("KillerPDF.pdf", window.Title);
        Assert.False(window.PageViews[^1].HasImage);
        Save(window, "kp-open.png");

        window.Zoom = 2;
        Assert.True(Pump(() => window.PageViews[0].Bounds.Width > 1000 && window.PageViews[0].RenderedScale == 2));
        Save(window, "kp-zoom200.png");
    }, CancellationToken.None);

    [Fact]
    public Task ScrollRendersLaterPagesAndDropsEarlierPages() => Headless.Dispatch(() =>
    {
        var window = Show();
        window.OpenFile(Sample);
        Assert.True(Pump(() => window.PageViews[0].HasImage));
        ScrollViewer viewer = window.Viewer;
        viewer.Offset = new Vector(0, viewer.Extent.Height);
        int last = window.PageViews.Count - 1;
        Assert.True(Pump(() => window.PageViews[last].HasImage));
        Assert.False(window.PageViews[0].HasImage);
        Assert.Equal($"page {FirstVisible(window)} / {last + 1}", window.PageText.Text);
        Save(window, "kp-scrolled.png");
    }, CancellationToken.None);

    [Fact]
    public Task CommandKeysZoomAndListSelectionScrolls() => Headless.Dispatch(() =>
    {
        var window = Show();
        window.OpenFile(Sample);
        Assert.True(Pump(() => window.PageViews[0].HasImage));
        window.KeyPress(Key.OemPlus, RawInputModifiers.Meta, PhysicalKey.Equal, "=");
        Assert.Equal(1.25, window.Zoom, 3);
        window.KeyPress(Key.OemMinus, RawInputModifiers.Meta, PhysicalKey.Minus, "-");
        Assert.Equal(1.0, window.Zoom, 3);
        window.KeyPress(Key.D0, RawInputModifiers.Meta, PhysicalKey.Digit0, "0");
        Assert.True(Pump(() => Math.Abs(window.PageViews[0].Bounds.Width - (window.Viewer.Viewport.Width - 32)) < 2));
        window.KeyPress(Key.OemPlus, RawInputModifiers.None, PhysicalKey.Equal, "=");
        Assert.NotEqual(1.25, window.Zoom, 3);

        window.PageList.Selection.Select(20);
        window.PageList.Selection.Select(4);
        Assert.Equal([4, 20], window.SelectedPages);
        Assert.True(Pump(() => window.PageText.Text == $"page 5 / {window.PageViews.Count}"));
    }, CancellationToken.None);

    [Fact]
    public Task GarbageFileShowsMessageAndKeepsWindow() => Headless.Dispatch(() =>
    {
        var window = Show();
        string bad = Path.Combine(TestPdf.TempDir(), "garbage.pdf");
        File.WriteAllText(bad, "this is not a pdf");
        window.OpenFile(bad);
        Dispatcher.UIThread.RunJobs();
        Assert.Null(window.Session);
        Assert.True(window.IsVisible);
        Window dialog = Assert.Single(window.OwnedWindows);
        Assert.Equal("Cannot open file", dialog.Title);
        Save(dialog, "kp-garbage.png");
    }, CancellationToken.None);

    [Fact]
    public Task FastScrollKeepsFewBitmaps() => Headless.Dispatch(() =>
    {
        string big = Path.Combine(TestPdf.TempDir(), "big.pdf");
        var builder = new PdfDocumentBuilder();
        for (int i = 0; i < 520; i++)
        {
            string ops = string.Concat(Enumerable.Range(0, 60).Select(k =>
                $"{(i * 7 + k * 13) % 255 / 255.0:0.00} 0.3 0.6 rg {k * 9 % 560} {k * 12 % 740} 40 30 re f "));
            builder.AddPage(612, 792, System.Text.Encoding.ASCII.GetBytes(ops));
        }
        File.WriteAllBytes(big, builder.Build());
        var window = Show();
        window.OpenFile(big);
        Assert.True(Pump(() => window.PageViews[0].HasImage));
        output.WriteLine($"pages {window.PageViews.Count}, rss after open {Rss()} MB");

        ScrollViewer viewer = window.Viewer;
        long peak = 0;
        int maxHeld = 0;
        for (double y = 0; y <= viewer.Extent.Height; y += viewer.Viewport.Height / 2)
        {
            viewer.Offset = new Vector(0, y);
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
            Dispatcher.UIThread.RunJobs();
            maxHeld = Math.Max(maxHeld, window.PageViews.Count(v => v.HasImage));
            peak = Math.Max(peak, Rss());
        }
        Assert.True(Pump(() => window.PageViews[^1].HasImage));
        GC.Collect();
        output.WriteLine($"peak rss {peak} MB, rss settled {Rss()} MB, most page bitmaps held {maxHeld}");
        Assert.True(maxHeld <= 12, $"held {maxHeld}");
        Assert.True(window.PageViews.Count(v => v.HasImage) <= 12);
    }, CancellationToken.None);

    private static int FirstVisible(MainWindow window) =>
        window.PageViews.First(v => v.TranslatePoint(default, window.Viewer) is { Y: var y } && y + v.Bounds.Height >= 0).PageIndex + 1;

    internal static MainWindow Show()
    {
        var window = new MainWindow { Width = 1100, Height = 800 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    internal static bool Pump(Func<bool> done)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(30))
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            if (done()) return true;
            Thread.Sleep(10);
        }
        return false;
    }

    internal static void Save(Window window, string name)
    {
        if (Shots is null) return;
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame()?.Save(Path.Combine(Shots, name));
    }

    private static long Rss()
    {
        using var process = Process.GetCurrentProcess();
        return process.WorkingSet64 / (1024 * 1024);
    }

    private static string RepoRoot()
    {
        string dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "KillerPDF.pdf"))) dir = Path.GetDirectoryName(dir)!;
        return dir;
    }
}
