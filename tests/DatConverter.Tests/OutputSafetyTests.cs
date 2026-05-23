namespace DatConverter.Tests;

public sealed class OutputSafetyTests
{
    [Fact]
    public void PlanOutputPath_ReturnsDirectPathEvenWhenOutputExists()
    {
        using var temp = new TempDirectory();
        var inputPath = Path.Combine(temp.Path, "name.dat");
        File.WriteAllText(inputPath, "source");
        File.WriteAllText(Path.Combine(temp.Path, "name.mp4"), "existing");

        var planned = OutputPathService.PlanOutputPath(inputPath, temp.Path, OutputFormat.Mp4);

        Assert.Equal(Path.Combine(temp.Path, "name.mp4"), planned);
    }

    [Fact]
    public void PlanOutputPath_AllowsSpecialCharactersAndUnicodeInBaseName()
    {
        using var temp = new TempDirectory();
        var fileName = "demo clip (front door) & John's camera - cafe \u65e5.dat";
        var inputPath = Path.Combine(temp.Path, fileName);
        File.WriteAllText(inputPath, "source");

        var planned = OutputPathService.PlanOutputPath(inputPath, temp.Path, OutputFormat.Mkv);

        Assert.Equal(Path.Combine(temp.Path, Path.GetFileNameWithoutExtension(fileName) + ".mkv"), planned);
    }

    [Fact]
    public void OutputPathGuard_RejectsSourcePathEvenWhenSpelledDifferently()
    {
        using var temp = new TempDirectory();
        var inputPath = Path.Combine(temp.Path, "clip.dat");
        File.WriteAllText(inputPath, "source");
        var samePathViaRelativeSegment = Path.Combine(temp.Path, ".", "clip.dat");

        Assert.False(OutputPathService.IsSafeOutputPath(inputPath, samePathViaRelativeSegment));
    }

    [Fact]
    public void ValidateCustomOutputPath_AppendsSelectedExtensionWhenMissing()
    {
        using var temp = new TempDirectory();
        var inputPath = Path.Combine(temp.Path, "source.dat");
        File.WriteAllText(inputPath, "source");

        var result = OutputPathService.ValidateCustomOutputPath(
            inputPath,
            Path.Combine(temp.Path, "renamed"),
            OutputFormat.Mkv,
            requireAvailable: true);

        Assert.True(result.IsValid);
        Assert.Equal(Path.Combine(temp.Path, "renamed.mkv"), result.OutputPath);
    }

    [Fact]
    public void ValidateCustomOutputPath_RejectsWrongExtension()
    {
        using var temp = new TempDirectory();
        var inputPath = Path.Combine(temp.Path, "source.dat");
        File.WriteAllText(inputPath, "source");

        var result = OutputPathService.ValidateCustomOutputPath(
            inputPath,
            Path.Combine(temp.Path, "renamed.mkv"),
            OutputFormat.Mp4,
            requireAvailable: true);

        Assert.False(result.IsValid);
        Assert.Contains(".mp4", result.Message);
    }

    [Fact]
    public void ValidateCustomOutputPath_RejectsExistingOutputWhenAvailabilityIsRequired()
    {
        using var temp = new TempDirectory();
        var inputPath = Path.Combine(temp.Path, "source.dat");
        var outputPath = Path.Combine(temp.Path, "renamed.mp4");
        File.WriteAllText(inputPath, "source");
        File.WriteAllText(outputPath, "existing");

        var result = OutputPathService.ValidateCustomOutputPath(
            inputPath,
            outputPath,
            OutputFormat.Mp4,
            requireAvailable: true);

        Assert.False(result.IsValid);
        Assert.Equal("A file by that name already exists.", result.Message);
    }

    [Fact]
    public void PartialCleanup_RenamesOnlyGeneratedOutputAndLeavesSourceUnchanged()
    {
        using var temp = new TempDirectory();
        var sourcePath = Path.Combine(temp.Path, "source.dat");
        var outputPath = Path.Combine(temp.Path, "source.mp4");
        File.WriteAllText(sourcePath, "original source bytes");
        var sourceTimestamp = File.GetLastWriteTimeUtc(sourcePath);
        File.WriteAllText(outputPath, "partial output bytes");

        var message = PartialOutputService.TryMovePartialOutput(outputPath, sourcePath);

        Assert.Contains(".partial", message);
        Assert.True(File.Exists(sourcePath));
        Assert.Equal("original source bytes", File.ReadAllText(sourcePath));
        Assert.Equal(sourceTimestamp, File.GetLastWriteTimeUtc(sourcePath));
        Assert.False(File.Exists(outputPath));
        Assert.True(File.Exists(outputPath + ".partial"));
    }

    [Fact]
    public void PartialCleanup_DoesNotMoveSourceWhenOutputPathMatchesSource()
    {
        using var temp = new TempDirectory();
        var sourcePath = Path.Combine(temp.Path, "source.dat");
        File.WriteAllText(sourcePath, "original source bytes");
        var sourceLength = new FileInfo(sourcePath).Length;
        var sourceTimestamp = File.GetLastWriteTimeUtc(sourcePath);

        var message = PartialOutputService.TryMovePartialOutput(sourcePath, sourcePath);

        Assert.Contains("skipped", message);
        Assert.True(File.Exists(sourcePath));
        Assert.Equal(sourceLength, new FileInfo(sourcePath).Length);
        Assert.Equal(sourceTimestamp, File.GetLastWriteTimeUtc(sourcePath));
        Assert.False(File.Exists(sourcePath + ".partial"));
    }

    [Fact]
    public async Task ConversionService_BlocksSourceAsOutputBeforeStartingFfmpeg()
    {
        using var temp = new TempDirectory();
        var sourcePath = Path.Combine(temp.Path, "source.dat");
        File.WriteAllText(sourcePath, "original source bytes");
        var sourceLength = new FileInfo(sourcePath).Length;
        var sourceTimestamp = File.GetLastWriteTimeUtc(sourcePath);
        var tools = new FfmpegTools(temp.Path, Path.Combine(temp.Path, "missing-ffmpeg.exe"), Path.Combine(temp.Path, "missing-ffprobe.exe"), false, false);
        var service = new ConversionService(tools);

        var result = await service.RemuxAsync(
            sourcePath,
            sourcePath,
            OutputFormat.Mp4,
            FpsOption.FromLabel("30"),
            null,
            null,
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("output path matches the source", result.UserMessage);
        Assert.True(File.Exists(sourcePath));
        Assert.Equal(sourceLength, new FileInfo(sourcePath).Length);
        Assert.Equal(sourceTimestamp, File.GetLastWriteTimeUtc(sourcePath));
        Assert.Equal("original source bytes", File.ReadAllText(sourcePath));
    }

    [Fact]
    public async Task ConversionService_BlocksExistingOutputBeforeStartingFfmpeg()
    {
        using var temp = new TempDirectory();
        var sourcePath = Path.Combine(temp.Path, "source.dat");
        var outputPath = Path.Combine(temp.Path, "source.mp4");
        File.WriteAllText(sourcePath, "original source bytes");
        File.WriteAllText(outputPath, "existing output bytes");
        var outputLength = new FileInfo(outputPath).Length;
        var outputTimestamp = File.GetLastWriteTimeUtc(outputPath);
        var tools = new FfmpegTools(temp.Path, Path.Combine(temp.Path, "missing-ffmpeg.exe"), Path.Combine(temp.Path, "missing-ffprobe.exe"), false, false);
        var service = new ConversionService(tools);

        var result = await service.RemuxAsync(
            sourcePath,
            outputPath,
            OutputFormat.Mp4,
            FpsOption.FromLabel("30"),
            null,
            null,
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("output file already exists", result.UserMessage);
        Assert.True(File.Exists(outputPath));
        Assert.Equal(outputLength, new FileInfo(outputPath).Length);
        Assert.Equal(outputTimestamp, File.GetLastWriteTimeUtc(outputPath));
        Assert.Equal("existing output bytes", File.ReadAllText(outputPath));
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
