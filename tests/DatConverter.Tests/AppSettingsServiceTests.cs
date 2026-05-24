namespace DatConverter.Tests;

public sealed class AppSettingsServiceTests
{
    [Fact]
    public void CreateDefault_UsesAutoDetectFps()
    {
        var settings = AppSettingsService.CreateDefault();

        Assert.Equal("Auto-detect", settings.Fps);
    }

    [Fact]
    public void Normalize_PreservesAutoDetectFps()
    {
        var settings = AppSettingsService.Normalize(new AppSettings { Fps = "Auto-detect" });

        Assert.Equal("Auto-detect", settings.Fps);
    }

    [Theory]
    [InlineData("15", "15")]
    [InlineData("20", "20")]
    [InlineData("24", "24")]
    [InlineData("25", "25")]
    [InlineData("29.97", "29.97")]
    [InlineData("30", "30")]
    public void Normalize_PreservesManualFpsChoices(string value, string expected)
    {
        var settings = AppSettingsService.Normalize(new AppSettings { Fps = value });

        Assert.Equal(expected, settings.Fps);
    }

    [Theory]
    [InlineData("Auto-detect")]
    [InlineData("25")]
    [InlineData("29.97")]
    public void Load_ResetsSavedFpsToStartupAutoDetectDefault(string savedFps)
    {
        using var temp = new TempDirectory();
        var settingsPath = Path.Combine(temp.Path, "settings.json");
        var service = new AppSettingsService(settingsPath);

        var saved = service.Save(new AppSettings { Fps = savedFps }, out var errorMessage);
        var loaded = service.Load(out _);

        Assert.True(saved, errorMessage);
        Assert.Equal("Auto-detect", loaded.Fps);
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "DatConverter.Tests." + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch
            {
            }
        }
    }
}
