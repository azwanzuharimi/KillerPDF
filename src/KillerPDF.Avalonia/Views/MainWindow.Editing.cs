using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace KillerPDF.Avalonia.Views;

public partial class MainWindow
{
    private static readonly FilePickerFileType PdfType = new("PDF") { Patterns = ["*.pdf"] };
    private bool _closeConfirmed;
    private bool _closePrompt;

    private int CurrentPage => SelectedPages.Count > 0 ? SelectedPages[0] : _pageCache.FirstVisible;
    private IReadOnlyList<int> TargetPages => SelectedPages.Count > 0 ? SelectedPages : [_pageCache.FirstVisible];

    private void UpdateCommands()
    {
        bool open = Session is not null;
        SaveButton.IsEnabled = MergeButton.IsEnabled = RotateLeftButton.IsEnabled =
            RotateRightButton.IsEnabled = DeleteButton.IsEnabled = open;
        MoveUpButton.IsEnabled = open && CurrentPage > 0;
        MoveDownButton.IsEnabled = open && CurrentPage < Session!.PageCount - 1;
        UndoButton.IsEnabled = Session?.CanUndo == true;
        RedoButton.IsEnabled = Session?.CanRedo == true;
    }

    private bool RunEdit(string title, Action edit)
    {
        if (Session is null) return false;
        try
        {
            edit();
            return true;
        }
        catch (Exception ex)
        {
            _ = ShowMessage(title, ex.Message);
            return false;
        }
    }

    internal void RotatePages(int degrees) => RunEdit("Cannot rotate", () => Session!.Rotate(TargetPages, degrees));

    internal void MovePage(int offset)
    {
        int from = CurrentPage;
        int to = from + offset;
        if (Session is null || to < 0 || to >= Session.PageCount) return;
        if (RunEdit("Cannot move page", () => Session.Move(from, to))) PageList.Selection.SelectedIndex = to;
    }

    internal async Task DeletePages(IReadOnlyList<int> pages)
    {
        if (Session is null || pages.Count == 0) return;
        if (await Ask("Delete pages", $"Delete {pages.Count} page(s)?", "Delete", "Cancel") != "Delete") return;
        RunEdit("Cannot delete", () => Session.Delete(pages));
    }

    internal void MergeFiles(IReadOnlyList<string> paths) => RunEdit("Cannot merge", () => Session!.Merge(paths));

    internal bool SaveDocument() => RunEdit("Cannot save", () => Session!.Save());

    internal void SaveDocumentAs(string path) => RunEdit("Cannot save", () => Session!.SaveAs(path));

    internal async Task<bool> ConfirmDiscard()
    {
        if (Session?.IsDirty != true) return true;
        string? answer = await Ask("Unsaved changes",
            $"Save changes to {Path.GetFileName(Session.FilePath)}?", "Save", "Don't Save", "Cancel");
        return answer switch
        {
            "Save" => SaveDocument(),
            "Don't Save" => true,
            _ => false,
        };
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (HoldClose()) e.Cancel = true;
    }

    internal void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        if (HoldClose()) e.Cancel = true;
    }

    private bool HoldClose()
    {
        if (_closeConfirmed || Session?.IsDirty != true) return false;
        if (!_closePrompt) _ = PromptClose();
        return true;
    }

    private async Task PromptClose()
    {
        _closePrompt = true;
        try
        {
            if (!await ConfirmDiscard()) return;
            _closeConfirmed = true;
            Close();
        }
        finally
        {
            _closePrompt = false;
        }
    }

    private void OnSave(object? sender, RoutedEventArgs e) => SaveDocument();

    private async void OnSaveAs()
    {
        if (Session is null) return;
        try
        {
            IStorageFile? file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save PDF As",
                SuggestedFileName = Path.GetFileName(Session.FilePath),
                DefaultExtension = "pdf",
                FileTypeChoices = [PdfType],
            });
            if (file?.TryGetLocalPath() is { } path) SaveDocumentAs(path);
        }
        catch (Exception ex)
        {
            await ShowMessage("Cannot save", ex.Message);
        }
    }

    private async void OnMerge(object? sender, RoutedEventArgs e)
    {
        if (Session is null) return;
        try
        {
            IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Merge PDFs",
                AllowMultiple = true,
                FileTypeFilter = [PdfType],
            });
            string[] paths = [.. files.Select(f => f.TryGetLocalPath()).OfType<string>()];
            if (paths.Length > 0) MergeFiles(paths);
        }
        catch (Exception ex)
        {
            await ShowMessage("Cannot merge", ex.Message);
        }
    }

    private void OnRotateLeft(object? sender, RoutedEventArgs e) => RotatePages(-90);
    private void OnRotateRight(object? sender, RoutedEventArgs e) => RotatePages(90);
    private async void OnDelete(object? sender, RoutedEventArgs e) => await DeletePages(TargetPages);
    private void OnMoveUp(object? sender, RoutedEventArgs e) => MovePage(-1);
    private void OnMoveDown(object? sender, RoutedEventArgs e) => MovePage(1);
    private void OnUndo(object? sender, RoutedEventArgs e) => RunEdit("Cannot undo", () => Session!.Undo());
    private void OnRedo(object? sender, RoutedEventArgs e) => RunEdit("Cannot redo", () => Session!.Redo());

    private void OnDragOver(object? sender, DragEventArgs e) =>
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;

    private void OnDrop(object? sender, DragEventArgs e)
    {
        string[] paths = [.. (e.DataTransfer.TryGetFiles() ?? [])
            .Select(f => f.TryGetLocalPath())
            .OfType<string>()
            .Where(p => p.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))];
        if (paths.Length == 0) return;
        e.Handled = true;
        if (Session is not null && e.KeyModifiers.HasFlag(KeyModifiers.Alt)) MergeFiles(paths);
        else if (paths.Length == 1) OpenFile(paths[0]);
        else _ = ShowMessage("Cannot open files", "Drop one PDF to open it. Hold Option while you drop to merge PDFs into the open document.");
    }

    private Task ShowMessage(string title, string message) => Ask(title, message, "OK");

    private async Task<string?> Ask(string title, string message, params string[] answers)
    {
        if (!IsVisible) await WhenOpened();
        var dialog = new Window
        {
            Title = title,
            Width = 420,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        for (int i = 0; i < answers.Length; i++)
        {
            string answer = answers[i];
            var button = new Button { Content = answer, IsDefault = i == 0, IsCancel = answer is "Cancel" || answers.Length == 1 };
            button.Click += (_, _) => dialog.Close(answer);
            row.Children.Add(button);
        }
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 12,
            Children = { new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap }, row },
        };
        return await dialog.ShowDialog<string?>(this);
    }

    private Task WhenOpened()
    {
        var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(object? sender, EventArgs e)
        {
            Opened -= Handler;
            opened.TrySetResult();
        }
        Opened += Handler;
        return opened.Task;
    }
}
