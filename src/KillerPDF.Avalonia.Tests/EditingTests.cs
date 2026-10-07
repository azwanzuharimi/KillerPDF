using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using KillerPDF.Avalonia.Views;
using static KillerPDF.Avalonia.Tests.MainWindowTests;

namespace KillerPDF.Avalonia.Tests;

public class EditingTests
{
    [Fact]
    public Task ToolbarEditsFollowSessionState() => Headless.Dispatch(() =>
    {
        var window = Show();
        Assert.False(window.SaveButton.IsEnabled);
        Assert.False(window.RotateLeftButton.IsEnabled);
        window.OpenFile(Sample3());
        Assert.NotNull(window.Session);
        Assert.True(window.RotateLeftButton.IsEnabled);
        Assert.False(window.UndoButton.IsEnabled);
        Assert.False(window.MoveUpButton.IsEnabled);
        Assert.True(window.MoveDownButton.IsEnabled);

        window.PageList.Selection.SelectedIndex = 1;
        Click(window.RotateRightButton);
        Assert.Equal([0, 90, 0], window.Session!.Pages.Select(p => p.Rotation));
        Assert.True(window.UndoButton.IsEnabled);
        Assert.True(window.Session.IsDirty);
        Click(window.RotateLeftButton);
        Assert.Equal([0, 0, 0], window.Session.Pages.Select(p => p.Rotation));

        window.PageList.Selection.SelectedIndex = 0;
        Click(window.MoveDownButton);
        Assert.Equal([400, 300, 500], window.Session.Pages.Select(p => p.Width));
        Assert.Equal([1], window.SelectedPages);
        Click(window.MoveUpButton);
        Assert.Equal([300, 400, 500], window.Session.Pages.Select(p => p.Width));

        window.PageList.Selection.SelectedIndex = 2;
        Assert.False(window.MoveDownButton.IsEnabled);
        Click(window.DeleteButton);
        Answer(window, "Delete pages", "Cancel");
        Assert.Equal(3, window.Session.PageCount);
        Click(window.DeleteButton);
        Window confirm = Answer(window, "Delete pages", "Delete");
        Assert.Equal("Delete 1 page(s)?", Texts(confirm).First());
        Assert.Equal([300, 400], window.Session.Pages.Select(p => p.Width));

        Click(window.UndoButton);
        Assert.Equal(3, window.Session.PageCount);
        Assert.True(window.RedoButton.IsEnabled);
        window.KeyPress(Key.Z, RawInputModifiers.Meta | RawInputModifiers.Shift, PhysicalKey.Z, "z");
        Assert.Equal(2, window.Session.PageCount);
        window.KeyPress(Key.Z, RawInputModifiers.Meta, PhysicalKey.Z, "z");
        Assert.Equal(3, window.Session.PageCount);
        Save(window, "kp-edit-toolbar.png");
    }, CancellationToken.None);

    [Fact]
    public Task DeleteEveryPageShowsMessageAndKeepsDocument() => Headless.Dispatch(() =>
    {
        var window = Show();
        window.OpenFile(Sample3());
        window.PageList.Selection.SelectAll();
        Click(window.DeleteButton);
        Answer(window, "Delete pages", "Delete");
        Window message = Answer(window, "Cannot delete", "OK");
        Assert.Contains("at least one page", Texts(message).First());
        Assert.Equal(3, window.Session!.PageCount);
        Assert.False(window.Session.IsDirty);
    }, CancellationToken.None);

    [Fact]
    public Task BackspaceDeletesOnlyWhenPageListHasFocus() => Headless.Dispatch(() =>
    {
        var window = Show();
        window.OpenFile(Sample3());
        window.PageList.Selection.SelectedIndex = 0;
        window.Viewer.Focus();
        window.KeyPress(Key.Back, RawInputModifiers.None, PhysicalKey.Backspace, "");
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(window.OwnedWindows);

        Assert.True(window.PageList.ContainerFromIndex(0)!.Focus());
        window.KeyPress(Key.Back, RawInputModifiers.None, PhysicalKey.Backspace, "");
        Answer(window, "Delete pages", "Delete");
        Assert.Equal([400, 500], window.Session!.Pages.Select(p => p.Width));
    }, CancellationToken.None);

    [Fact]
    public Task SaveCommandsWriteFileAndFailureKeepsState() => Headless.Dispatch(() =>
    {
        var window = Show();
        string path = Sample3();
        window.OpenFile(path);
        window.PageList.Selection.SelectedIndex = 0;
        Click(window.RotateRightButton);

        window.SaveDocumentAs(Path.Combine(TestPdf.TempDir(), "missing", "out.pdf"));
        Answer(window, "Cannot save", "OK");
        Assert.True(window.Session!.IsDirty);
        Assert.Equal([0, 0, 0], TestPdf.Rotations(path));

        window.KeyPress(Key.S, RawInputModifiers.Meta, PhysicalKey.S, "s");
        Assert.False(window.Session.IsDirty);
        Assert.Equal([90, 0, 0], TestPdf.Rotations(path));
        Assert.Equal("", window.DirtyText.Text);
    }, CancellationToken.None);

    [Fact]
    public Task UnsavedPromptCancelKeepsWindowAndDocument() => Headless.Dispatch(() =>
    {
        var window = Show();
        string first = Sample3();
        window.OpenFile(first);
        window.PageList.Selection.SelectedIndex = 0;
        Click(window.RotateRightButton);
        var session = window.Session;

        window.Close();
        Answer(window, "Unsaved changes", "Cancel");
        Assert.True(window.IsVisible);
        Assert.Same(session, window.Session);
        Assert.True(session!.IsDirty);

        string second = TestPdf.Create(Path.Combine(TestPdf.TempDir(), "two.pdf"), 200);
        window.OpenFile(second);
        Answer(window, "Unsaved changes", "Cancel");
        Assert.Same(session, window.Session);

        window.OpenFile(second);
        Answer(window, "Unsaved changes", "Save");
        Assert.Equal(1, window.Session!.PageCount);
        Assert.Equal([90, 0, 0], TestPdf.Rotations(first));

        window.PageList.Selection.SelectedIndex = 0;
        Click(window.RotateRightButton);
        window.Close();
        Answer(window, "Unsaved changes", "Don't Save");
        Assert.False(window.IsVisible);
        Assert.Equal([0], TestPdf.Rotations(second));
    }, CancellationToken.None);

    [Fact]
    public Task MessageBeforeWindowOpensWaitsForOpened() => Headless.Dispatch(() =>
    {
        var window = new MainWindow { Width = 1100, Height = 800 };
        string bad = Path.Combine(TestPdf.TempDir(), "garbage.pdf");
        File.WriteAllText(bad, "this is not a pdf");
        window.OpenFile(bad);
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(window.OwnedWindows);
        window.Show();
        Answer(window, "Cannot open file", "OK");
        Assert.Null(window.Session);
    }, CancellationToken.None);

    [Fact]
    public Task DropOpensFileAndOptionDropMerges() => Headless.Dispatch(() =>
    {
        var window = Show();
        string first = Sample3();
        string second = TestPdf.Create(Path.Combine(TestPdf.TempDir(), "two.pdf"), 200, 250);
        Drop(window, RawInputModifiers.None, first);
        Assert.True(Pump(() => window.Session is not null));
        Assert.Equal(3, window.Session!.PageCount);

        Drop(window, RawInputModifiers.Alt, second, second);
        Assert.Equal([300, 400, 500, 200, 250, 200, 250], window.Session.Pages.Select(p => p.Width));

        window.KeyPress(Key.Z, RawInputModifiers.Meta, PhysicalKey.Z, "z");
        Drop(window, RawInputModifiers.None, second);
        Assert.Equal([200, 250], window.Session!.Pages.Select(p => p.Width));
    }, CancellationToken.None);

    private static string Sample3() => TestPdf.Create(Path.Combine(TestPdf.TempDir(), "three.pdf"), 300, 400, 500);

    private static void Click(Button button)
    {
        Assert.True(button.IsEnabled, $"{button.Content} is disabled");
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    private static Window Answer(MainWindow window, string title, string answer)
    {
        Assert.True(Pump(() => window.OwnedWindows.Any(w => w.Title == title)), $"no dialog {title}");
        Window dialog = window.OwnedWindows.Single(w => w.Title == title);
        Save(dialog, $"kp-dialog-{title.Replace(' ', '-').ToLowerInvariant()}.png");
        Button button = dialog.GetVisualDescendants().OfType<Button>().Single(b => (string?)b.Content == answer);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(Pump(() => !window.OwnedWindows.Contains(dialog)));
        Dispatcher.UIThread.RunJobs();
        return dialog;
    }

    private static IEnumerable<string> Texts(Window dialog) =>
        dialog.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").Where(t => t.Length > 0);

    private static IStorageFile LocalFile(MainWindow window, string path)
    {
        Task<IStorageFile?> file = window.StorageProvider.TryGetFileFromPathAsync(path);
        Assert.True(Pump(() => file.IsCompleted));
        return file.Result ?? throw new InvalidOperationException("headless storage provider returned no file");
    }

    private static void Drop(MainWindow window, RawInputModifiers modifiers, params string[] paths)
    {
        var data = new DataTransfer();
        foreach (string path in paths)
        {
            var item = new DataTransferItem();
            item.SetFile(LocalFile(window, path));
            data.Add(item);
        }
        var point = new Point(500, 400);
        window.DragDrop(point, RawDragEventType.DragEnter, data, DragDropEffects.Copy, modifiers);
        window.DragDrop(point, RawDragEventType.DragOver, data, DragDropEffects.Copy, modifiers);
        window.DragDrop(point, RawDragEventType.Drop, data, DragDropEffects.Copy, modifiers);
        Dispatcher.UIThread.RunJobs();
    }
}
