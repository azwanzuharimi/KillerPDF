using KillerPdf.Engine.Fonts;
using KillerPDF.Services;

namespace KillerPDF.Avalonia.Core;

public sealed class MacFontResolver(IReadOnlyList<string> fontDirectories) : IPdfFontResolver
{
    public static MacFontResolver Instance { get; } = new([
        "/System/Library/Fonts/Supplemental",
        "/System/Library/Fonts",
        "/Library/Fonts",
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library/Fonts"),
    ]);

    private readonly Dictionary<PdfFontRequest, byte[]?> _cache = [];
    private readonly Lock _gate = new();

    public byte[]? Resolve(PdfFontRequest request)
    {
        lock (_gate)
        {
            if (_cache.TryGetValue(request, out byte[]? cached)) return cached;
            DetectedPdfFontStyle style = PdfFontStyle.FromPdfName(request.PostScriptName);
            string face = (style.Bold, style.Italic) switch
            {
                (true, true) => " Bold Italic",
                (true, false) => " Bold",
                (false, true) => " Italic",
                _ => "",
            };
            byte[]? bytes = null;
            foreach (string directory in fontDirectories)
            {
                foreach (string extension in new[] { ".ttf", ".otf" })
                {
                    string path = Path.Combine(directory, style.Family + face + extension);
                    if (!File.Exists(path)) continue;
                    try { bytes = File.ReadAllBytes(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                    if (bytes is not null) break;
                }
                if (bytes is not null) break;
            }
            _cache[request] = bytes;
            return bytes;
        }
    }
}
