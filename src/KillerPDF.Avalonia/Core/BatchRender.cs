using System.Diagnostics;
using System.Globalization;

namespace KillerPDF.Avalonia.Core;

public static class BatchRender
{
    public static int Run(string folder, string csvPath, double scale, TextWriter log)
    {
        bool failed = false;
        using var csv = new StreamWriter(csvPath);
        csv.WriteLine("file,page,width,height,ms,error");
        foreach (string file in Directory.EnumerateFiles(folder, "*.pdf", SearchOption.AllDirectories).Order())
        {
            string name = Path.GetRelativePath(folder, file);
            try
            {
                PdfSession session = PdfSession.Open(file);
                var rasterizer = new PageRasterizer(session.Document);
                for (int i = 0; i < session.PageCount; i++)
                {
                    var watch = Stopwatch.StartNew();
                    try
                    {
                        RasterPage page = rasterizer.Render(i, scale, CancellationToken.None);
                        csv.WriteLine(Row(name, i, page.Width, page.Height, watch.ElapsedMilliseconds, ""));
                    }
                    catch (Exception ex)
                    {
                        failed = true;
                        csv.WriteLine(Row(name, i, 0, 0, watch.ElapsedMilliseconds, ex.GetType().Name + ": " + ex.Message));
                    }
                }
            }
            catch (Exception ex)
            {
                failed = true;
                csv.WriteLine(Row(name, -1, 0, 0, 0, ex.GetType().Name + ": " + ex.Message));
            }
            log.WriteLine(name);
        }
        return failed ? 1 : 0;
    }

    private static string Row(string file, int page, int w, int h, long ms, string error) =>
        string.Join(',', Quote(file), page.ToString(CultureInfo.InvariantCulture), w, h, ms, Quote(error));

    private static string Quote(string value) =>
        value.IndexOfAny([',', '"', '\n', '\r']) < 0 ? value : "\"" + value.Replace("\"", "\"\"") + "\"";
}
