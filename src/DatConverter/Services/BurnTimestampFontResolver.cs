namespace DatConverter;

public static class BurnTimestampFontResolver
{
    public const string MissingPreferredFontWarning = "Burn timestamp font file was not found; using FFmpeg default font.";

    private static readonly string[] PreferredFontFileNames =
    [
        "consolab.ttf",
        "consola.ttf",
        "arialbd.ttf"
    ];

    public static BurnTimestampFontResolution Resolve()
    {
        return Resolve(@"C:\Windows\Fonts", File.Exists);
    }

    public static BurnTimestampFontResolution Resolve(string fontsDirectory, Func<string, bool> fileExists)
    {
        foreach (var fontFileName in PreferredFontFileNames)
        {
            var path = Path.Combine(fontsDirectory, fontFileName);
            if (fileExists(path))
            {
                return new BurnTimestampFontResolution(path, null);
            }
        }

        return new BurnTimestampFontResolution(null, MissingPreferredFontWarning);
    }
}

public sealed record BurnTimestampFontResolution(
    string? FontFilePath,
    string? Warning);
