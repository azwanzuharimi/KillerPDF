using KillerPdf.Engine.Authoring;
using KillerPdf.Engine.Documents;

namespace KillerPDF.Avalonia.Tests;

internal static class TestPdf
{
    internal static string Create(string path, params double[] pageWidths)
    {
        var builder = new PdfDocumentBuilder();
        foreach (double width in pageWidths) builder.AddPage(width, 500, ReadOnlyMemory<byte>.Empty);
        File.WriteAllBytes(path, builder.Build());
        return path;
    }

    internal static double[] Widths(string path) =>
        [.. PdfPageInformation.Read(PdfDocument.Open(File.ReadAllBytes(path))).Select(p => p.Width)];

    internal static int[] Rotations(string path) =>
        [.. PdfPageInformation.Read(PdfDocument.Open(File.ReadAllBytes(path))).Select(p => p.Rotation)];

    internal static string TempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "kp-mac-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
