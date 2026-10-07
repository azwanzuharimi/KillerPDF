using System.Globalization;
using KillerPDF.Avalonia.Core;

namespace KillerPDF.Avalonia;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        int folder = Array.IndexOf(args, "--render-folder");
        if (folder >= 0)
        {
            int outIndex = Array.IndexOf(args, "--out");
            int scaleIndex = Array.IndexOf(args, "--scale");
            if (folder + 1 >= args.Length || outIndex < 0 || outIndex + 1 >= args.Length)
            {
                Console.Error.WriteLine("Usage: KillerPDF --render-folder <dir> --out <csv> [--scale 1.5]");
                return 2;
            }
            double scale = scaleIndex >= 0 && scaleIndex + 1 < args.Length
                ? double.Parse(args[scaleIndex + 1], CultureInfo.InvariantCulture) : 1.5;
            return BatchRender.Run(args[folder + 1], args[outIndex + 1], scale, Console.Out);
        }
        return 0;
    }
}
