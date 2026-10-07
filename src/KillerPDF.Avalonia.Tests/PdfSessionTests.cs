using KillerPDF.Avalonia.Core;

namespace KillerPDF.Avalonia.Tests;

public class PdfSessionTests
{
    private readonly string _dir = TestPdf.TempDir();

    [Fact]
    public void OpenReadsPagesAndIsClean()
    {
        string file = TestPdf.Create(Path.Combine(_dir, "a.pdf"), 100, 110, 120);
        PdfSession session = PdfSession.Open(file);
        Assert.Equal(3, session.PageCount);
        Assert.Equal([100d, 110d, 120d], session.Pages.Select(p => p.Width));
        Assert.False(session.IsDirty);
        Assert.Equal(Path.GetFullPath(file), session.FilePath);
    }

    [Fact]
    public void OpenOfGarbageThrows()
    {
        string file = Path.Combine(_dir, "bad.pdf");
        File.WriteAllText(file, "this is not a pdf");
        Assert.ThrowsAny<Exception>(() => PdfSession.Open(file));
    }

    [Fact]
    public void SaveAsWritesFileAndClearsDirty()
    {
        string file = TestPdf.Create(Path.Combine(_dir, "a.pdf"), 100, 110);
        PdfSession session = PdfSession.Open(file);
        string target = Path.Combine(_dir, "out.pdf");
        session.SaveAs(target);
        Assert.Equal([100d, 110d], TestPdf.Widths(target));
        Assert.Equal(target, session.FilePath);
        Assert.False(session.IsDirty);
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }

    [Fact]
    public void SaveAsToMissingFolderThrowsAndKeepsState()
    {
        string file = TestPdf.Create(Path.Combine(_dir, "a.pdf"), 100);
        PdfSession session = PdfSession.Open(file);
        Assert.ThrowsAny<IOException>(() => session.SaveAs(Path.Combine(_dir, "missing", "x.pdf")));
        Assert.Equal(Path.GetFullPath(file), session.FilePath);
    }
}
