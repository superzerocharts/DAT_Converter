namespace DatConverter.Tests;

public sealed class ConversionServiceInternalCleanRemuxTests
{
    [Fact]
    public async Task RemuxAsync_DefaultOptions_UsesCleanExtractedInputAndDeletesIt()
    {
        using var temp = new TempDirectory();
        var inputPath = Path.Combine(temp.Path, "clip.dat");
        var outputPath = Path.Combine(temp.Path, "clip.mp4");
        File.WriteAllText(inputPath, "source");
        string? tempInputPath = null;
        var service = CreateService(
            InternalConversionPathOptions.Default,
            (_, tempH264Path, _) =>
            {
                File.WriteAllText(tempH264Path, "clean");
                return CreateExtractionResult(tempH264Path, confident: true);
            },
            (_, arguments, _, _, _, _) =>
            {
                tempInputPath = GetOptionValue(arguments, "-i");
                File.WriteAllText(outputPath, "output");
                return Task.FromResult(new ProcessRunResult(0, false, false, "", ""));
            });

        var result = await service.RemuxAsync(inputPath, outputPath, OutputFormat.Mp4, FpsOption.FromLabel("30"), null, null, CancellationToken.None);

        Assert.True(result.IsSuccess, result.StandardError);
        Assert.Equal(ConversionInputPathMode.CleanExtractedH264, result.InputPathMode);
        Assert.NotNull(tempInputPath);
        Assert.EndsWith(".internal-clean-remux.h264", tempInputPath, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(tempInputPath!));
        Assert.NotEqual(inputPath, tempInputPath);
    }

    [Fact]
    public async Task RemuxAsync_KillSwitchEnabled_UsesStandardWholeDatInput()
    {
        using var temp = new TempDirectory();
        var inputPath = Path.Combine(temp.Path, "clip.dat");
        var outputPath = Path.Combine(temp.Path, "clip.mp4");
        File.WriteAllText(inputPath, "source");
        IReadOnlyList<string>? capturedArguments = null;
        var service = CreateService(
            new InternalConversionPathOptions(DisableCleanRemux: true),
            (_, _, _) => throw new InvalidOperationException("Extractor should not run."),
            (_, arguments, _, _, _, _) =>
            {
                capturedArguments = arguments;
                File.WriteAllText(outputPath, "output");
                return Task.FromResult(new ProcessRunResult(0, false, false, "", ""));
            });

        var result = await service.RemuxAsync(inputPath, outputPath, OutputFormat.Mp4, FpsOption.FromLabel("30"), null, null, CancellationToken.None);

        Assert.True(result.IsSuccess, result.StandardError);
        Assert.Equal(ConversionInputPathMode.StandardWholeDatRawH264, result.InputPathMode);
        Assert.NotNull(capturedArguments);
        Assert.Equal(inputPath, GetOptionValue(capturedArguments!, "-i"));
    }

    [Fact]
    public async Task EncodeAsync_InternalCleanRemuxEnabled_StillUsesStandardEncodeInput()
    {
        using var temp = new TempDirectory();
        var inputPath = Path.Combine(temp.Path, "clip.dat");
        var outputPath = Path.Combine(temp.Path, "clip.mp4");
        File.WriteAllText(inputPath, "source");
        IReadOnlyList<string>? capturedArguments = null;
        var service = CreateService(
            InternalConversionPathOptions.Default,
            (_, _, _) => throw new InvalidOperationException("Extractor should not run for encode."),
            (_, arguments, _, _, _, _) =>
            {
                capturedArguments = arguments;
                File.WriteAllText(outputPath, "output");
                return Task.FromResult(new ProcessRunResult(0, false, false, "", ""));
            });

        var result = await service.EncodeAsync(inputPath, outputPath, OutputFormat.Mp4, FpsOption.FromLabel("30"), null, null, CancellationToken.None);

        Assert.True(result.IsSuccess, result.StandardError);
        Assert.Equal(ConversionInputPathMode.StandardWholeDatRawH264, result.InputPathMode);
        Assert.NotNull(capturedArguments);
        Assert.Equal(inputPath, GetOptionValue(capturedArguments!, "-i"));
        Assert.Contains("-vf", capturedArguments!);
    }

    [Fact]
    public async Task RemuxAsync_WeakExtractionFallsBackToStandardPathAndDeletesTemp()
    {
        using var temp = new TempDirectory();
        var inputPath = Path.Combine(temp.Path, "clip.dat");
        var outputPath = Path.Combine(temp.Path, "clip.mp4");
        File.WriteAllText(inputPath, "source");
        string? tempH264Path = null;
        IReadOnlyList<string>? capturedArguments = null;
        var service = CreateService(
            InternalConversionPathOptions.Default,
            (_, tempPath, _) =>
            {
                tempH264Path = tempPath;
                File.WriteAllText(tempPath, "weak");
                return CreateExtractionResult(tempPath, confident: false);
            },
            (_, arguments, _, _, _, _) =>
            {
                capturedArguments = arguments;
                File.WriteAllText(outputPath, "output");
                return Task.FromResult(new ProcessRunResult(0, false, false, "", ""));
            });

        var result = await service.RemuxAsync(inputPath, outputPath, OutputFormat.Mkv, FpsOption.FromLabel("30"), null, null, CancellationToken.None);

        Assert.True(result.IsSuccess, result.StandardError);
        Assert.Equal(ConversionInputPathMode.StandardWholeDatRawH264, result.InputPathMode);
        Assert.Contains("fallback", result.StandardError, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(capturedArguments);
        Assert.Equal(inputPath, GetOptionValue(capturedArguments!, "-i"));
        Assert.NotNull(tempH264Path);
        Assert.False(File.Exists(tempH264Path!));
    }

    [Fact]
    public async Task RemuxAsync_CleanPathFailureDeletesTemp()
    {
        using var temp = new TempDirectory();
        var inputPath = Path.Combine(temp.Path, "clip.dat");
        var outputPath = Path.Combine(temp.Path, "clip.mp4");
        File.WriteAllText(inputPath, "source");
        string? tempH264Path = null;
        var service = CreateService(
            InternalConversionPathOptions.Default,
            (_, tempPath, _) =>
            {
                tempH264Path = tempPath;
                File.WriteAllText(tempPath, "clean");
                return CreateExtractionResult(tempPath, confident: true);
            },
            (_, _, _, _, _, _) => Task.FromResult(new ProcessRunResult(1, false, false, "", "failed")));

        var result = await service.RemuxAsync(inputPath, outputPath, OutputFormat.Mp4, FpsOption.FromLabel("30"), null, null, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ConversionInputPathMode.CleanExtractedH264, result.InputPathMode);
        Assert.NotNull(tempH264Path);
        Assert.False(File.Exists(tempH264Path!));
    }

    [Fact]
    public async Task RemuxAsync_CleanPathCanceledDeletesTemp()
    {
        using var temp = new TempDirectory();
        var inputPath = Path.Combine(temp.Path, "clip.dat");
        var outputPath = Path.Combine(temp.Path, "clip.mp4");
        File.WriteAllText(inputPath, "source");
        string? tempH264Path = null;
        var service = CreateService(
            InternalConversionPathOptions.Default,
            (_, tempPath, _) =>
            {
                tempH264Path = tempPath;
                File.WriteAllText(tempPath, "clean");
                return CreateExtractionResult(tempPath, confident: true);
            },
            (_, _, _, _, _, _) => Task.FromResult(new ProcessRunResult(null, false, true, "", "Process was canceled.")));

        var result = await service.RemuxAsync(inputPath, outputPath, OutputFormat.Mp4, FpsOption.FromLabel("30"), null, null, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.True(result.WasCanceled);
        Assert.Equal(ConversionInputPathMode.CleanExtractedH264, result.InputPathMode);
        Assert.NotNull(tempH264Path);
        Assert.False(File.Exists(tempH264Path!));
    }

    [Fact]
    public async Task RemuxAsync_CanceledDuringExtractionDeletesTempBeforeFfmpegStarts()
    {
        using var temp = new TempDirectory();
        var inputPath = Path.Combine(temp.Path, "clip.dat");
        var outputPath = Path.Combine(temp.Path, "clip.mp4");
        File.WriteAllText(inputPath, "source");
        string? tempH264Path = null;
        var ffmpegCalled = false;
        var service = CreateService(
            InternalConversionPathOptions.Default,
            (_, tempPath, _) =>
            {
                tempH264Path = tempPath;
                File.WriteAllText(tempPath, "partial");
                throw new OperationCanceledException();
            },
            (_, _, _, _, _, _) =>
            {
                ffmpegCalled = true;
                return Task.FromResult(new ProcessRunResult(0, false, false, "", ""));
            });
        var result = await service.RemuxAsync(inputPath, outputPath, OutputFormat.Mp4, FpsOption.FromLabel("30"), null, null, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.True(result.WasCanceled);
        Assert.False(ffmpegCalled);
        Assert.Equal(ConversionInputPathMode.CleanExtractedH264, result.InputPathMode);
        Assert.NotNull(tempH264Path);
        Assert.False(File.Exists(tempH264Path!));
    }

    [Fact]
    public async Task RemuxAsync_CleanPathUsesPerItemFps()
    {
        using var temp = new TempDirectory();
        var inputPath = Path.Combine(temp.Path, "clip.dat");
        var outputPath = Path.Combine(temp.Path, "clip.mp4");
        File.WriteAllText(inputPath, "source");
        IReadOnlyList<string>? capturedArguments = null;
        var service = CreateService(
            InternalConversionPathOptions.Default,
            (_, tempPath, _) =>
            {
                File.WriteAllText(tempPath, "clean");
                return CreateExtractionResult(tempPath, confident: true);
            },
            (_, arguments, _, _, _, _) =>
            {
                capturedArguments = arguments;
                File.WriteAllText(outputPath, "output");
                return Task.FromResult(new ProcessRunResult(0, false, false, "", ""));
            });

        var result = await service.RemuxAsync(inputPath, outputPath, OutputFormat.Mp4, FpsOption.FromLabel("29.97"), null, null, CancellationToken.None);

        Assert.True(result.IsSuccess, result.StandardError);
        Assert.Equal(ConversionInputPathMode.CleanExtractedH264, result.InputPathMode);
        Assert.NotNull(capturedArguments);
        Assert.Equal("30000/1001", GetOptionValue(capturedArguments!, "-r"));
    }

    [Fact]
    public async Task RemuxAsync_CleanPathLogsInternalUseAndDiskSpaceNote()
    {
        using var temp = new TempDirectory();
        var inputPath = Path.Combine(temp.Path, "clip.dat");
        var outputPath = Path.Combine(temp.Path, "clip.mp4");
        File.WriteAllText(inputPath, "source");
        var service = CreateService(
            InternalConversionPathOptions.Default,
            (_, tempPath, _) =>
            {
                File.WriteAllText(tempPath, "clean");
                return CreateExtractionResult(tempPath, confident: true);
            },
            (_, _, _, _, _, _) =>
            {
                File.WriteAllText(outputPath, "output");
                return Task.FromResult(new ProcessRunResult(0, false, false, "", "ffmpeg note"));
            });

        var result = await service.RemuxAsync(inputPath, outputPath, OutputFormat.Mp4, FpsOption.FromLabel("30"), null, null, CancellationToken.None);

        Assert.True(result.IsSuccess, result.StandardError);
        Assert.Contains("Internal clean remux path used", result.StandardError);
        Assert.Contains("Temporary disk space", result.StandardError);
        Assert.Contains("ffmpeg note", result.StandardError);
    }

    private static ConversionService CreateService(
        InternalConversionPathOptions options,
        Func<string, string, CancellationToken, SpotterDatPayloadExtractionResult> extract,
        Func<string, IReadOnlyList<string>, TimeSpan, CancellationToken, Action<string>?, Action<string>?, Task<ProcessRunResult>> runProcessAsync)
    {
        return new ConversionService(
            new FfmpegTools(AppContext.BaseDirectory, "ffmpeg.exe", "ffprobe.exe", FfmpegExists: true, FfprobeExists: true),
            options,
            extract,
            runProcessAsync);
    }

    private static SpotterDatPayloadExtractionResult CreateExtractionResult(string outputPath, bool confident)
    {
        return new SpotterDatPayloadExtractionResult
        {
            Succeeded = true,
            InputPath = Path.ChangeExtension(outputPath, ".dat"),
            OutputPath = outputPath,
            InputFileSize = 100,
            ExtractedPayloadByteCount = 80,
            FrameRecordCount = 1,
            ExtractedFrameRecordCount = 1,
            CandidateNalUnitCount = 3,
            SpsCount = 1,
            PpsCount = 1,
            IdrFrameCount = 1,
            LookedConfident = confident
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
