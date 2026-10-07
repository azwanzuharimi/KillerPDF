using KillerPdf.Engine.Fonts;
using KillerPDF.Avalonia.Core;

namespace KillerPDF.Avalonia.Tests;

public class MacFontResolverTests
{
    private static PdfFontRequest Req(string name) => new(name, "", "", false);

    [Fact]
    public void ResolvesStandardAliasesToFileBytes()
    {
        if (!OperatingSystem.IsMacOS()) return;
        byte[]? regular = MacFontResolver.Instance.Resolve(Req("Helvetica"));
        byte[]? bold = MacFontResolver.Instance.Resolve(Req("ArialMT,Bold"));
        Assert.NotNull(regular);
        Assert.NotNull(bold);
        Assert.NotEqual(regular!.Length, bold!.Length);
    }

    [Fact]
    public void FindsFaceInGivenFolderBySubsetName()
    {
        string dir = TestPdf.TempDir();
        File.WriteAllBytes(Path.Combine(dir, "Courier New Bold Italic.ttf"), [1, 2, 3]);
        var resolver = new MacFontResolver([dir]);
        Assert.Equal([1, 2, 3], resolver.Resolve(Req("ABCDEF+CourierNewPS-BoldItalicMT")));
    }

    [Fact]
    public void UnknownFontReturnsNull()
    {
        var resolver = new MacFontResolver([TestPdf.TempDir()]);
        Assert.Null(resolver.Resolve(Req("NoSuchFont-Regular")));
    }
}
