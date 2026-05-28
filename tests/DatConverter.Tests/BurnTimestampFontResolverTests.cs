namespace DatConverter.Tests;

public sealed class BurnTimestampFontResolverTests
{
    [Fact]
    public void Resolve_ChoosesConsolasBoldWhenPresent()
    {
        var result = ResolveWithExisting("consolab.ttf", "consola.ttf", "arialbd.ttf");

        Assert.EndsWith(Path.Combine("Fonts", "consolab.ttf"), result.FontFilePath);
        Assert.Null(result.Warning);
    }

    [Fact]
    public void Resolve_FallsBackToConsolasRegularWhenBoldIsMissing()
    {
        var result = ResolveWithExisting("consola.ttf", "arialbd.ttf");

        Assert.EndsWith(Path.Combine("Fonts", "consola.ttf"), result.FontFilePath);
        Assert.Null(result.Warning);
    }

    [Fact]
    public void Resolve_FallsBackToArialBoldWhenConsolasFilesAreMissing()
    {
        var result = ResolveWithExisting("arialbd.ttf");

        Assert.EndsWith(Path.Combine("Fonts", "arialbd.ttf"), result.FontFilePath);
        Assert.Null(result.Warning);
    }

    [Fact]
    public void Resolve_MissingPreferredFontsReturnsWarning()
    {
        var result = ResolveWithExisting();

        Assert.Null(result.FontFilePath);
        Assert.Equal(BurnTimestampFontResolver.MissingPreferredFontWarning, result.Warning);
    }

    private static BurnTimestampFontResolution ResolveWithExisting(params string[] existingFileNames)
    {
        var existing = existingFileNames
            .Select(name => Path.Combine(@"C:\Windows\Fonts", name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return BurnTimestampFontResolver.Resolve(@"C:\Windows\Fonts", existing.Contains);
    }
}
