namespace DatConverter.Tests;

public sealed class SpotterCleanRemuxPrototypeTests
{
    [Fact]
    public void BuildCleanRemuxArguments_Mp4UsesPrototypeCleanRemuxFlags()
    {
        var arguments = SpotterCleanRemuxPrototype.BuildCleanRemuxArguments(
            @"C:\temp\clip.h264",
            @"C:\out\clip.mp4",
            OutputFormat.Mp4,
            FpsOption.FromLabel("30"));

        Assert.Equal(
            new[]
            {
                "-n",
                "-fflags",
                "+genpts+discardcorrupt",
                "-err_detect",
                "ignore_err",
                "-f",
                "h264",
                "-r",
                "30",
                "-i",
                @"C:\temp\clip.h264",
                "-c:v",
                "copy",
                "-movflags",
                "+faststart",
                @"C:\out\clip.mp4"
            },
            arguments);
    }

    [Fact]
    public void BuildCleanRemuxArguments_MkvDoesNotUseFaststart()
    {
        var arguments = SpotterCleanRemuxPrototype.BuildCleanRemuxArguments(
            @"C:\temp\clip.h264",
            @"C:\out\clip.mkv",
            OutputFormat.Mkv,
            FpsOption.FromLabel("25"));

        Assert.DoesNotContain("-movflags", arguments);
        Assert.DoesNotContain("+faststart", arguments);
        Assert.Equal("25", GetOptionValue(arguments, "-r"));
    }

    [Fact]
    public async Task RunAsync_WeakExtractionFailsBeforeFfmpeg()
    {
        using var temp = new TempDirectory();
        var inputPath = Path.Combine(temp.Path, "clip.dat");
        var outputPath = Path.Combine(temp.Path, "clip.mp4");
        var ffmpegPath = Path.Combine(temp.Path, "ffmpeg.exe");
        File.WriteAllText(inputPath, "not relevant");
        File.WriteAllText(ffmpegPath, "");
        var ffmpegCalled = false;
        var prototype = new SpotterCleanRemuxPrototype(
            (_, tempH264Path) =>
            {
                File.WriteAllText(tempH264Path, "partial");
                return CreateExtractionResult(succeeded: true, confident: false, tempH264Path);
            },
            (_, _, _, _) =>
            {
                ffmpegCalled = true;
                return Task.FromResult(new ProcessRunResult(0, false, false, "", ""));
            });

        var result = await prototype.RunAsync(inputPath, outputPath, ffmpegPath, FpsOption.FromLabel("30"), keepTemp: false, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.False(ffmpegCalled);
        Assert.Contains("not confident", result.FailureReason);
        Assert.False(Directory.EnumerateFiles(temp.Path, "*.prototype.h264").Any());
    }

    [Fact]
    public async Task RunAsync_SuccessCreatesOutputAndDeletesTempByDefault()
    {
        using var temp = new TempDirectory();
        var inputPath = Path.Combine(temp.Path, "clip.dat");
        var outputPath = Path.Combine(temp.Path, "clip.mp4");
        var ffmpegPath = Path.Combine(temp.Path, "ffmpeg.exe");
        File.WriteAllText(inputPath, "not relevant");
        File.WriteAllText(ffmpegPath, "");
        string? tempSeenByFfmpeg = null;
        var prototype = new SpotterCleanRemuxPrototype(
            (_, tempH264Path) =>
            {
                File.WriteAllText(tempH264Path, "clean");
                return CreateExtractionResult(succeeded: true, confident: true, tempH264Path);
            },
            (_, arguments, _, _) =>
            {
                tempSeenByFfmpeg = GetOptionValue(arguments, "-i");
                File.WriteAllText(outputPath, "mp4");
                return Task.FromResult(new ProcessRunResult(0, false, false, "ok", ""));
            });

        var result = await prototype.RunAsync(inputPath, outputPath, ffmpegPath, FpsOption.FromLabel("30"), keepTemp: false, CancellationToken.None);

        Assert.True(result.Succeeded, result.FailureReason);
        Assert.True(File.Exists(outputPath));
        Assert.NotNull(tempSeenByFfmpeg);
        Assert.False(File.Exists(tempSeenByFfmpeg!));
        Assert.Null(result.TempH264Path);
        Assert.Contains("FFmpeg stdout", result.BuildTechnicalReport());
    }

    [Fact]
    public async Task RunAsync_KeepTempLeavesExtractedStream()
    {
        using var temp = new TempDirectory();
        var inputPath = Path.Combine(temp.Path, "clip.dat");
        var outputPath = Path.Combine(temp.Path, "clip.mkv");
        var ffmpegPath = Path.Combine(temp.Path, "ffmpeg.exe");
        File.WriteAllText(inputPath, "not relevant");
        File.WriteAllText(ffmpegPath, "");
        var prototype = new SpotterCleanRemuxPrototype(
            (_, tempH264Path) =>
            {
                File.WriteAllText(tempH264Path, "clean");
                return CreateExtractionResult(succeeded: true, confident: true, tempH264Path);
            },
            (_, _, _, _) =>
            {
                File.WriteAllText(outputPath, "mkv");
                return Task.FromResult(new ProcessRunResult(0, false, false, "", ""));
            });

        var result = await prototype.RunAsync(inputPath, outputPath, ffmpegPath, FpsOption.FromLabel("30"), keepTemp: true, CancellationToken.None);

        Assert.True(result.Succeeded, result.FailureReason);
        Assert.True(result.KeptTempFile);
        Assert.NotNull(result.TempH264Path);
        Assert.True(File.Exists(result.TempH264Path!));
    }

    [Fact]
    public async Task RunAsync_FfmpegFailureReportsTechnicalDetailsAndDeletesOutput()
    {
        using var temp = new TempDirectory();
        var inputPath = Path.Combine(temp.Path, "clip.dat");
        var outputPath = Path.Combine(temp.Path, "clip.mp4");
        var ffmpegPath = Path.Combine(temp.Path, "ffmpeg.exe");
        File.WriteAllText(inputPath, "not relevant");
        File.WriteAllText(ffmpegPath, "");
        var prototype = new SpotterCleanRemuxPrototype(
            (_, tempH264Path) =>
            {
                File.WriteAllText(tempH264Path, "clean");
                return CreateExtractionResult(succeeded: true, confident: true, tempH264Path);
            },
            (_, _, _, _) =>
            {
                File.WriteAllText(outputPath, "partial");
                return Task.FromResult(new ProcessRunResult(1, false, false, "", "bad stream"));
            });

        var result = await prototype.RunAsync(inputPath, outputPath, ffmpegPath, FpsOption.FromLabel("30"), keepTemp: false, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("FFmpeg", result.FailureReason);
        Assert.Contains("bad stream", result.BuildTechnicalReport());
        Assert.False(File.Exists(outputPath));
    }

    [Fact]
    public async Task RunAsync_ExistingOutputIsNotOverwritten()
    {
        using var temp = new TempDirectory();
        var inputPath = Path.Combine(temp.Path, "clip.dat");
        var outputPath = Path.Combine(temp.Path, "clip.mp4");
        var ffmpegPath = Path.Combine(temp.Path, "ffmpeg.exe");
        File.WriteAllText(inputPath, "not relevant");
        File.WriteAllText(outputPath, "existing");
        File.WriteAllText(ffmpegPath, "");
        var prototype = new SpotterCleanRemuxPrototype(
            (_, _) => throw new InvalidOperationException("Extraction should not run."),
            (_, _, _, _) => throw new InvalidOperationException("FFmpeg should not run."));

        var result = await prototype.RunAsync(inputPath, outputPath, ffmpegPath, FpsOption.FromLabel("30"), keepTemp: false, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("already exists", result.FailureReason);
        Assert.Equal("existing", File.ReadAllText(outputPath));
    }

    private static SpotterDatPayloadExtractionResult CreateExtractionResult(bool succeeded, bool confident, string outputPath)
    {
        return new SpotterDatPayloadExtractionResult
        {
            Succeeded = succeeded,
            InputPath = Path.ChangeExtension(outputPath, ".dat"),
            OutputPath = outputPath,
            InputFileSize = 100,
            ExtractedPayloadByteCount = 80,
            FrameRecordCount = 1,
            ExtractedFrameRecordCount = succeeded ? 1 : 0,
            CandidateNalUnitCount = succeeded ? 3 : 0,
            SpsCount = succeeded ? 1 : 0,
            PpsCount = succeeded ? 1 : 0,
            IdrFrameCount = succeeded ? 1 : 0,
            LookedConfident = confident,
            FailureReason = succeeded ? null : "failed"
        };
    }

    private static string GetOptionValue(IReadOnlyList<string> arguments, string option)
    {
        var index = arguments.ToList().IndexOf(option);
        Assert.True(index >= 0 && index < arguments.Count - 1, $"Missing option value for {option}.");
        return arguments[index + 1];
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
