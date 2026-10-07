using KillerPDF.Avalonia.Core;

namespace KillerPDF.Avalonia.Tests;

public class BatchRenderTests
{
    [Fact]
    public void WritesRowPerPageAndFailsOnBadFile()
    {
        string dir = TestPdf.TempDir();
        TestPdf.Create(Path.Combine(dir, "good.pdf"), 100, 110);
        File.WriteAllText(Path.Combine(dir, "bad.pdf"), "garbage");
        string csv = Path.Combine(dir, "out.csv");
        int code = BatchRender.Run(dir, csv, 1, TextWriter.Null);
        string[] lines = File.ReadAllLines(csv);
        Assert.Equal(1, code);
        Assert.Equal("file,page,width,height,ms,error", lines[0]);
        Assert.Equal(2, lines.Count(l => l.StartsWith("good.pdf,")));
        Assert.Contains(lines, l => l.StartsWith("bad.pdf,-1,") && l.Length > "bad.pdf,-1,0,0,0,".Length);
    }

    [Fact]
    public void FormulaFileNameIsQuotedWithApostrophe()
    {
        string dir = TestPdf.TempDir();
        TestPdf.Create(Path.Combine(dir, "=cmd.pdf"), 100);
        string csv = Path.Combine(dir, "out.csv");
        BatchRender.Run(dir, csv, 1, TextWriter.Null);
        Assert.StartsWith("\"'=cmd.pdf\",0,", File.ReadAllLines(csv)[1]);
    }
}
