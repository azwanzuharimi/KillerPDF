using KillerPdf.Engine.Documents;

namespace KillerPDF.Avalonia.Core;

public sealed partial class PdfSession
{
    private byte[] _bytes = [];
    private byte[] _savedBytes = [];

    private PdfSession(string path, byte[] bytes)
    {
        FilePath = Path.GetFullPath(path);
        Load(bytes);
        _savedBytes = bytes;
    }

    public string FilePath { get; private set; }
    public PdfDocument Document { get; private set; } = null!;
    public IReadOnlyList<PdfPageInformation> Pages { get; private set; } = [];
    public int PageCount => Pages.Count;
    public bool IsDirty => !ReferenceEquals(_bytes, _savedBytes);
    public int Version { get; private set; }
    public event EventHandler? Changed;

    public static PdfSession Open(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return new PdfSession(path, File.ReadAllBytes(path));
    }

    public void Save() => SaveAs(FilePath);

    public void SaveAs(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string full = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(full)!;
        string temp = Path.Combine(directory, $".{Path.GetFileName(full)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(temp, _bytes);
            File.Move(temp, full, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
        FilePath = full;
        _savedBytes = _bytes;
        Raise();
    }

    private void Load(byte[] bytes)
    {
        PdfDocument document = PdfDocument.OpenWithCompatibilityRecovery(bytes);
        IReadOnlyList<PdfPageInformation> pages = PdfPageInformation.Read(document);
        if (pages.Count == 0) throw new InvalidDataException("The PDF contains no pages.");
        Document = document;
        Pages = pages;
        _bytes = bytes;
        Version++;
    }

    private void Raise() => Changed?.Invoke(this, EventArgs.Empty);
}
