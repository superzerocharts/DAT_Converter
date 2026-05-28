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
    public void PlanUniqueOutputPath_AllowsDuplicateSourceWhenOutputPathIsUnique()
    {
        using var temp = new TempDirectory();
        var inputPath = Path.Combine(temp.Path, "clip.dat");
        File.WriteAllText(inputPath, "source");
        var queuedOutputs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.Combine(temp.Path, "clip.mp4")
        };

        var planned = OutputPathService.PlanUniqueOutputPath(
            inputPath,
            temp.Path,
            OutputFormat.Mp4,
            candidate => !queuedOutputs.Contains(candidate));

        Assert.Equal(Path.Combine(temp.Path, "clip_01.mp4"), planned);
    }

    [Fact]
    public void PlanUniqueOutputPath_AllowsSameSourceWithDifferentOutputFormat()
    {
        using var temp = new TempDirectory();
        var inputPath = Path.Combine(temp.Path, "clip.dat");
        File.WriteAllText(inputPath, "source");
        var queuedOutputs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.Combine(temp.Path, "clip.mp4")
        };

        var planned = OutputPathService.PlanUniqueOutputPath(
            inputPath,
            temp.Path,
            OutputFormat.Mkv,
            candidate => !queuedOutputs.Contains(candidate));

        Assert.Equal(Path.Combine(temp.Path, "clip.mkv"), planned);
    }

    [Fact]
    public void PlanUniqueOutputPath_DoesNotReturnQueuedOutputPath()
    {
        using var temp = new TempDirectory();
        var inputPath = Path.Combine(temp.Path, "clip.dat");
        File.WriteAllText(inputPath, "source");
        var queuedOutputs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.Combine(temp.Path, "clip.mp4"),
            Path.Combine(temp.Path, "clip_01.mp4")
        };

        var planned = OutputPathService.PlanUniqueOutputPath(
            inputPath,
            temp.Path,
            OutputFormat.Mp4,
            candidate => !queuedOutputs.Contains(candidate));

        Assert.Equal(Path.Combine(temp.Path, "clip_02.mp4"), planned);
    }

    [Fact]
    public void PlanUniqueOutputPath_PreservesExistingDirectOutputWhenNotQueued()
    {
        using var temp = new TempDirectory();
        var inputPath = Path.Combine(temp.Path, "clip.dat");
        var existingOutputPath = Path.Combine(temp.Path, "clip.mp4");
        File.WriteAllText(inputPath, "source");
        File.WriteAllText(existingOutputPath, "existing");

        var planned = OutputPathService.PlanUniqueOutputPath(
            inputPath,
            temp.Path,
            OutputFormat.Mp4,
            _ => true);

        Assert.Equal(existingOutputPath, planned);
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
    public void CanceledOutputCleanup_DeletesGeneratedOutputAndLeavesSourceUnchanged()
    {
        using var temp = new TempDirectory();
        var sourcePath = Path.Combine(temp.Path, "source.dat");
        var outputPath = Path.Combine(temp.Path, "source.mp4");
        File.WriteAllText(sourcePath, "original source bytes");
        var sourceTimestamp = File.GetLastWriteTimeUtc(sourcePath);
        File.WriteAllText(outputPath, "canceled output bytes");

        var message = PartialOutputService.TryDeleteCanceledOutput(outputPath, sourcePath);

        Assert.Contains("deleted", message);
        Assert.True(File.Exists(sourcePath));
        Assert.Equal("original source bytes", File.ReadAllText(sourcePath));
        Assert.Equal(sourceTimestamp, File.GetLastWriteTimeUtc(sourcePath));
        Assert.False(File.Exists(outputPath));
    }

    [Fact]
    public void CanceledOutputCleanup_DeletesGeneratedPartialOutput()
    {
        using var temp = new TempDirectory();
        var sourcePath = Path.Combine(temp.Path, "source.dat");
        var outputPath = Path.Combine(temp.Path, "source.mp4");
        File.WriteAllText(sourcePath, "original source bytes");
        File.WriteAllText(outputPath + ".partial", "canceled partial bytes");

        var message = PartialOutputService.TryDeleteCanceledOutput(outputPath, sourcePath);

        Assert.Contains("partial output", message);
        Assert.False(File.Exists(outputPath + ".partial"));
        Assert.True(File.Exists(sourcePath));
    }

    [Fact]
    public void CanceledOutputCleanup_CanPreservePreExistingPartialOutput()
    {
        using var temp = new TempDirectory();
        var sourcePath = Path.Combine(temp.Path, "source.dat");
        var outputPath = Path.Combine(temp.Path, "source.mp4");
        File.WriteAllText(sourcePath, "original source bytes");
        File.WriteAllText(outputPath + ".partial", "older partial bytes");

        var message = PartialOutputService.TryDeleteCanceledOutput(outputPath, sourcePath, deleteSidecarPartial: false);

        Assert.Contains("No canceled output", message);
        Assert.True(File.Exists(outputPath + ".partial"));
        Assert.Equal("older partial bytes", File.ReadAllText(outputPath + ".partial"));
    }

    [Fact]
    public void CanceledOutputCleanup_DoesNotDeleteSourceWhenOutputPathMatchesSource()
    {
        using var temp = new TempDirectory();
        var sourcePath = Path.Combine(temp.Path, "source.dat");
        File.WriteAllText(sourcePath, "original source bytes");
        var sourceLength = new FileInfo(sourcePath).Length;
        var sourceTimestamp = File.GetLastWriteTimeUtc(sourcePath);

        var message = PartialOutputService.TryDeleteCanceledOutput(sourcePath, sourcePath);

        Assert.Contains("skipped", message);
        Assert.True(File.Exists(sourcePath));
        Assert.Equal(sourceLength, new FileInfo(sourcePath).Length);
        Assert.Equal(sourceTimestamp, File.GetLastWriteTimeUtc(sourcePath));
        Assert.Equal("original source bytes", File.ReadAllText(sourcePath));
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

    [Fact]
    public async Task ConversionService_BlocksUnresolvedFpsBeforeStartingFfmpeg()
    {
        using var temp = new TempDirectory();
        var sourcePath = Path.Combine(temp.Path, "source.dat");
        var outputPath = Path.Combine(temp.Path, "source.mp4");
        File.WriteAllText(sourcePath, "original source bytes");
        var tools = new FfmpegTools(temp.Path, Path.Combine(temp.Path, "missing-ffmpeg.exe"), Path.Combine(temp.Path, "missing-ffprobe.exe"), false, false);
        var service = new ConversionService(tools);

        var result = await service.RemuxAsync(
            sourcePath,
            outputPath,
            OutputFormat.Mp4,
            new FpsOption("Needs FPS", ""),
            null,
            null,
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("Source FPS is not set", result.UserMessage);
        Assert.Empty(result.Arguments);
        Assert.False(File.Exists(outputPath));
    }

    [Fact]
    public async Task ConversionService_EncodeReportsCanonicalFullModeWhenBlockedBeforeFfmpeg()
    {
        using var temp = new TempDirectory();
        var sourcePath = Path.Combine(temp.Path, "source.dat");
        var outputPath = Path.Combine(temp.Path, "source.mp4");
        File.WriteAllText(sourcePath, "original source bytes");
        var tools = new FfmpegTools(temp.Path, Path.Combine(temp.Path, "missing-ffmpeg.exe"), Path.Combine(temp.Path, "missing-ffprobe.exe"), false, false);
        var service = new ConversionService(tools);

        var result = await service.EncodeAsync(
            sourcePath,
            outputPath,
            OutputFormat.Mp4,
            new FpsOption("Needs FPS", ""),
            null,
            null,
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("Encode", result.ConversionMode);
        Assert.Empty(result.Arguments);
        Assert.False(File.Exists(outputPath));
    }

    [Fact]
    public async Task ConversionService_BurnTimestampFailureReportsBundledFfmpegMessage()
    {
        using var temp = new TempDirectory();
        var sourcePath = Path.Combine(temp.Path, "source.dat");
        var outputPath = Path.Combine(temp.Path, "source.mp4");
        File.WriteAllText(sourcePath, "original source bytes");
        var tools = new FfmpegTools(temp.Path, Path.Combine(temp.Path, "ffmpeg.exe"), Path.Combine(temp.Path, "ffprobe.exe"), true, true);
        var service = new ConversionService(
            tools,
            InternalConversionPathOptions.Default,
            (_, _, _) => throw new InvalidOperationException("Clean extraction should not run for direct encode."),
            (_, _, _, _, _, _) => Task.FromResult(new ProcessRunResult(1, false, false, "", "No such filter: 'drawtext'")));

        var result = await service.EncodeAsync(
            sourcePath,
            outputPath,
            OutputFormat.Mp4,
            FpsOption.FromLabel("30"),
            null,
            null,
            CancellationToken.None,
            burnTimestamp: new BurnTimestampOptions("Camera", new DateTime(2026, 5, 22, 4, 40, 12)));

        Assert.False(result.IsSuccess);
        Assert.Equal(BurnTimestampMetadataBuilder.BundledFfmpegUnavailableMessage, result.UserMessage);
        Assert.Contains("drawtext", string.Join(" ", result.Arguments));
        Assert.False(File.Exists(outputPath));
    }

    [Fact]
    public async Task ConversionService_BurnTimestampMissingFontWarningIsPreserved()
    {
        using var temp = new TempDirectory();
        var sourcePath = Path.Combine(temp.Path, "source.dat");
        var outputPath = Path.Combine(temp.Path, "source.mp4");
        File.WriteAllText(sourcePath, "original source bytes");
        var tools = new FfmpegTools(temp.Path, Path.Combine(temp.Path, "ffmpeg.exe"), Path.Combine(temp.Path, "ffprobe.exe"), true, true);
        var service = new ConversionService(
            tools,
            InternalConversionPathOptions.Default,
            (_, _, _) => throw new InvalidOperationException("Clean extraction should not run for direct encode."),
            (_, _, _, _, _, _) =>
            {
                File.WriteAllText(outputPath, "output");
                return Task.FromResult(new ProcessRunResult(0, false, false, "", ""));
            });

        var result = await service.EncodeAsync(
            sourcePath,
            outputPath,
            OutputFormat.Mp4,
            FpsOption.FromLabel("30"),
            null,
            null,
            CancellationToken.None,
            burnTimestamp: new BurnTimestampOptions(
                "Camera",
                new DateTime(2026, 5, 22, 4, 40, 12),
                FontWarning: BurnTimestampFontResolver.MissingPreferredFontWarning));

        Assert.True(result.IsSuccess, result.StandardError);
        Assert.Contains(BurnTimestampFontResolver.MissingPreferredFontWarning, result.StandardError);
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
