using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using KillerPDF.Avalonia.Views;

namespace KillerPDF.Avalonia;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            desktop.MainWindow = window;
            desktop.ShutdownRequested += window.OnShutdownRequested;
            string? path = desktop.Args?.FirstOrDefault(a => a.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase));
            if (path is not null) window.Opened += (_, _) => window.OpenFile(path);
            if (this.TryGetFeature<IActivatableLifetime>() is { } activatable)
            {
                activatable.Activated += (_, e) =>
                {
                    if (e is FileActivatedEventArgs files && files.Files.FirstOrDefault()?.TryGetLocalPath() is { } file)
                        window.OpenFile(file);
                };
            }
        }
        base.OnFrameworkInitializationCompleted();
    }
}
