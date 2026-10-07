using KillerPDF.Avalonia.Core;

namespace KillerPDF.Avalonia.Tests;

public class PdfSessionEditingTests
{
    private readonly string _dir = TestPdf.TempDir();

    private PdfSession Open(params double[] widths) =>
        PdfSession.Open(TestPdf.Create(Path.Combine(_dir, $"{Guid.NewGuid():N}.pdf"), widths));

    [Fact]
    public void RotateChangesOnlySelectedPages()
    {
        PdfSession s = Open(100, 110, 120);
        s.Rotate([1], 90);
        Assert.Equal([0, 90, 0], s.Pages.Select(p => p.Rotation));
        Assert.True(s.IsDirty);
        s.Rotate([1], -90);
        Assert.Equal(0, s.Pages[1].Rotation);
    }

    [Fact]
    public void DeleteRemovesPagesAndKeepsOrder()
    {
        PdfSession s = Open(100, 110, 120, 130);
        s.Delete([0, 2]);
        Assert.Equal([110d, 130d], s.Pages.Select(p => p.Width));
    }

    [Fact]
    public void DeleteOfEveryPageIsRejected()
    {
        PdfSession s = Open(100, 110);
        Assert.Throws<InvalidOperationException>(() => s.Delete([0, 1]));
        Assert.Equal(2, s.PageCount);
        Assert.False(s.IsDirty);
    }

    [Fact]
    public void MovePutsPageAtFinalIndex()
    {
        PdfSession s = Open(100, 110, 120);
        s.Move(0, 2);
        Assert.Equal([110d, 120d, 100d], s.Pages.Select(p => p.Width));
    }

    [Fact]
    public void MergeAppendsPagesInOrder()
    {
        PdfSession s = Open(100);
        string b = TestPdf.Create(Path.Combine(_dir, "b.pdf"), 200, 210);
        string c = TestPdf.Create(Path.Combine(_dir, "c.pdf"), 300);
        s.Merge([b, c]);
        Assert.Equal([100d, 200d, 210d, 300d], s.Pages.Select(p => p.Width));
    }

    [Fact]
    public void UndoAndRedoRestoreStateAndDirtyFlag()
    {
        PdfSession s = Open(100, 110, 120);
        s.Delete([0]);
        s.Move(0, 1);
        s.Undo();
        Assert.Equal([110d, 120d], s.Pages.Select(p => p.Width));
        s.Undo();
        Assert.Equal([100d, 110d, 120d], s.Pages.Select(p => p.Width));
        Assert.False(s.IsDirty);
        Assert.False(s.CanUndo);
        s.Redo();
        Assert.Equal([110d, 120d], s.Pages.Select(p => p.Width));
        Assert.True(s.IsDirty);
    }

    [Fact]
    public void NewEditClearsRedo()
    {
        PdfSession s = Open(100, 110);
        s.Rotate([0], 90);
        s.Undo();
        s.Rotate([1], 90);
        Assert.False(s.CanRedo);
    }

    [Fact]
    public void UndoHistoryIsCapped()
    {
        PdfSession s = Open(100, 110);
        for (int i = 0; i < PdfSession.UndoLimit + 5; i++) s.Rotate([0], 90);
        int undone = 0;
        while (s.CanUndo) { s.Undo(); undone++; }
        Assert.Equal(PdfSession.UndoLimit, undone);
    }

    [Fact]
    public void OriginalFileIsUnchangedUntilSave()
    {
        string file = TestPdf.Create(Path.Combine(_dir, "orig.pdf"), 100, 110, 120);
        byte[] before = File.ReadAllBytes(file);
        PdfSession s = PdfSession.Open(file);
        s.Delete([1]);
        s.Rotate([0], 90);
        Assert.Equal(before, File.ReadAllBytes(file));
        s.Save();
        Assert.Equal([100d, 120d], TestPdf.Widths(file));
        Assert.Equal([90, 0], TestPdf.Rotations(file));
    }

    [Fact]
    public void FailedSaveKeepsDirtyStateAndPages()
    {
        PdfSession s = Open(100, 110, 120);
        string path = s.FilePath;
        s.Delete([1]);
        Assert.True(s.IsDirty);
        Assert.ThrowsAny<IOException>(() => s.SaveAs(Path.Combine(_dir, "missing", "out.pdf")));
        Assert.True(s.IsDirty);
        Assert.Equal(path, s.FilePath);
        Assert.Equal([100d, 120d], s.Pages.Select(p => p.Width));
    }
}
