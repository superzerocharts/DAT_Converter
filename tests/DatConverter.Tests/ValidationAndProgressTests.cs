namespace DatConverter.Tests;

public sealed class ValidationAndProgressTests
{
    [Fact]
    public void InputValidation_RejectsNonDatMissingAndZeroByteFiles()
    {
        using var temp = new TempDirectory();
        var nonDatPath = Path.Combine(temp.Path, "clip.mp4");
        var zeroByteDatPath = Path.Combine(temp.Path, "empty.dat");
        File.WriteAllText(nonDatPath, "not a dat");
        File.Create(zeroByteDatPath).Dispose();

        Assert.False(InputFileValidator.ValidateDatFile(nonDatPath).IsValid);
        Assert.False(InputFileValidator.ValidateDatFile(Path.Combine(temp.Path, "missing.dat")).IsValid);
        Assert.False(InputFileValidator.ValidateDatFile(zeroByteDatPath).IsValid);
    }

    [Fact]
    public void InputValidation_UsesReadOnlyAccessAndLeavesDatMetadataUnchanged()
    {
        using var temp = new TempDirectory();
        var inputPath = Path.Combine(temp.Path, "valid.dat");
        File.WriteAllText(inputPath, "readable source bytes");
        var length = new FileInfo(inputPath).Length;
        var timestamp = File.GetLastWriteTimeUtc(inputPath);

        var result = InputFileValidator.ValidateDatFile(inputPath);

        Assert.True(result.IsValid);
        Assert.Equal(length, new FileInfo(inputPath).Length);
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(inputPath));
        Assert.Equal("readable source bytes", File.ReadAllText(inputPath));
    }

    [Fact]
    public void ProgressParser_ClampsProgressAtOneHundred()
    {
        var parser = new ConversionProgressParser(TimeSpan.FromSeconds(10));

        Assert.Null(parser.ParseLine("out_time_us=15000000"));
        var progress = parser.ParseLine("progress=continue");

        Assert.NotNull(progress);
        Assert.Equal(100, progress.Percent);
    }

    [Fact]
    public void ProgressParser_UsesUnknownPercentWhenDurationIsUnknown()
    {
        var parser = new ConversionProgressParser(null);

        Assert.Null(parser.ParseLine("out_time_us=15000000"));
        var progress = parser.ParseLine("progress=continue");

        Assert.NotNull(progress);
        Assert.Null(progress.Percent);
        Assert.Contains("output time", progress.Summary);
    }

    [Fact]
    public void ToolPathResolution_UsesBundledRelativeToolLocationsOnly()
    {
        var tools = ToolPathService.ResolveBundledTools();

        Assert.EndsWith(Path.Combine("tools", "ffmpeg", "ffmpeg.exe"), tools.FfmpegPath, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(Path.Combine("tools", "ffmpeg", "ffprobe.exe"), tools.FfprobePath, StringComparison.OrdinalIgnoreCase);
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
