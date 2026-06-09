namespace DatConverter.Tests;

public sealed class NvencCapabilityServiceTests
{
    [Fact]
    public void Detect_EncoderListedAndSmokeSucceeds_EnablesFullNvenc()
    {
        using var temp = new TempDirectory();
        var smokePath = Path.Combine(temp.Path, "smoke.mp4");
        var service = CreateService(
            temp.Path,
            smokePath,
            (executable, arguments) =>
            {
                if (IsNvidiaSmi(executable))
                {
                    return Success("NVIDIA RTX A4000, 555.12");
                }

                if (arguments.Contains("-encoders"))
                {
                    return Success(" V....D h264_nvenc NVIDIA NVENC H.264 encoder");
                }

                if (arguments.Contains("encoder=h264_nvenc"))
                {
                    return Success("Encoder h264_nvenc [NVIDIA NVENC H.264 encoder]");
                }

                File.WriteAllText(smokePath, "video");
                return Success("", "smoke ok");
            });

        var result = service.Detect(CreateTools(temp.Path));

        Assert.True(result.IsAvailable);
        Assert.True(result.EncoderListed);
        Assert.True(result.EncoderHelpAvailable);
        Assert.True(result.NvidiaSmiAvailable);
        Assert.True(result.SmokeTestSucceeded);
        Assert.Equal("Full NVENC available.", result.UserFacingStatus);
        Assert.Contains("final app decision: Full NVENC enabled", result.TechnicalDetails);
        Assert.Contains("nvidia-smi output summary: NVIDIA RTX A4000", result.TechnicalDetails);
    }

    [Fact]
    public void Detect_EncoderListedAndSmokeFails_DisablesFullNvencWithSmokeReason()
    {
        using var temp = new TempDirectory();
        var smokePath = Path.Combine(temp.Path, "smoke.mp4");
        var service = CreateService(
            temp.Path,
            smokePath,
            (executable, arguments) =>
            {
                if (IsNvidiaSmi(executable))
                {
                    return Success("NVIDIA RTX A4000, 555.12");
                }

                if (arguments.Contains("-encoders"))
                {
                    return Success("h264_nvenc");
                }

                if (arguments.Contains("encoder=h264_nvenc"))
                {
                    return Success("Encoder h264_nvenc");
                }

                return Failure("Cannot load nvcuda.dll");
            });

        var result = service.Detect(CreateTools(temp.Path));

        Assert.False(result.IsAvailable);
        Assert.True(result.EncoderListed);
        Assert.False(result.SmokeTestSucceeded);
        Assert.Equal("Full NVENC unavailable: h264_nvenc smoke test failed.", result.UserFacingStatus);
        Assert.Contains("smoke test exit code: 1", result.TechnicalDetails);
        Assert.Contains("Cannot load nvcuda.dll", result.TechnicalDetails);
        Assert.Contains("final app decision: Full NVENC disabled", result.TechnicalDetails);
    }

    [Fact]
    public void Detect_EncoderNotListed_DisablesFullNvencWithEncoderMissingReason()
    {
        using var temp = new TempDirectory();
        var service = CreateService(
            temp.Path,
            Path.Combine(temp.Path, "smoke.mp4"),
            (executable, arguments) =>
            {
                if (IsNvidiaSmi(executable))
                {
                    return Success("NVIDIA RTX A4000, 555.12");
                }

                return arguments.Contains("-encoders")
                    ? Success("libx264")
                    : Success("");
            });

        var result = service.Detect(CreateTools(temp.Path));

        Assert.False(result.IsAvailable);
        Assert.False(result.EncoderListed);
        Assert.Equal("Full NVENC unavailable: bundled FFmpeg does not list h264_nvenc.", result.UserFacingStatus);
        Assert.Contains("h264_nvenc listed: no", result.TechnicalDetails);
    }

    [Fact]
    public void Detect_FfmpegMissing_DisablesFullNvencWithFfmpegMissingReason()
    {
        using var temp = new TempDirectory();
        var service = CreateService(
            temp.Path,
            Path.Combine(temp.Path, "smoke.mp4"),
            (_, _) => Success(""));

        var result = service.Detect(new FfmpegTools(temp.Path, Path.Combine(temp.Path, "missing-ffmpeg.exe"), Path.Combine(temp.Path, "ffprobe.exe"), FfmpegExists: false, FfprobeExists: true));

        Assert.False(result.IsAvailable);
        Assert.False(result.EncoderListed);
        Assert.Equal("Full NVENC unavailable: bundled ffmpeg.exe was not found.", result.UserFacingStatus);
        Assert.Contains("ffmpeg exists: no", result.TechnicalDetails);
    }

    [Fact]
    public void Detect_NvidiaSmiMissingDoesNotDisableWhenSmokeSucceeds()
    {
        using var temp = new TempDirectory();
        var smokePath = Path.Combine(temp.Path, "smoke.mp4");
        var service = CreateService(
            temp.Path,
            smokePath,
            (executable, arguments) =>
            {
                if (IsNvidiaSmi(executable))
                {
                    return Failure("'nvidia-smi' is not recognized");
                }

                if (arguments.Contains("-encoders"))
                {
                    return Success("h264_nvenc");
                }

                if (arguments.Contains("encoder=h264_nvenc"))
                {
                    return Success("Encoder h264_nvenc");
                }

                File.WriteAllText(smokePath, "video");
                return Success("");
            });

        var result = service.Detect(CreateTools(temp.Path));

        Assert.True(result.IsAvailable);
        Assert.False(result.NvidiaSmiAvailable);
        Assert.True(result.SmokeTestSucceeded);
        Assert.Contains("nvidia-smi available: no", result.TechnicalDetails);
    }

    [Fact]
    public void Detect_NvidiaSmiAvailableButSmokeFailsStillDisablesFullNvenc()
    {
        using var temp = new TempDirectory();
        var service = CreateService(
            temp.Path,
            Path.Combine(temp.Path, "smoke.mp4"),
            (executable, arguments) =>
            {
                if (IsNvidiaSmi(executable))
                {
                    return Success("NVIDIA RTX A4000, 555.12");
                }

                if (arguments.Contains("-encoders"))
                {
                    return Success("h264_nvenc");
                }

                if (arguments.Contains("encoder=h264_nvenc"))
                {
                    return Success("Encoder h264_nvenc");
                }

                return Failure("OpenEncodeSessionEx failed");
            });

        var result = service.Detect(CreateTools(temp.Path));

        Assert.False(result.IsAvailable);
        Assert.True(result.NvidiaSmiAvailable);
        Assert.False(result.SmokeTestSucceeded);
        Assert.Equal("Full NVENC unavailable: h264_nvenc smoke test failed.", result.UserFacingStatus);
    }

    [Fact]
    public void DisplayModes_KeepFullNvencOptionAvailableForUiButSettingsNormalizeDisablesWhenUnavailable()
    {
        Assert.Contains(ConversionModes.FullNvencDisplayName, ConversionModes.DisplayOrder);

        var unavailable = AppSettingsService.Normalize(new AppSettings { ConversionMode = ConversionModes.FullNvencDisplayName }, nvencAvailable: false);
        var available = AppSettingsService.Normalize(new AppSettings { ConversionMode = ConversionModes.FullNvencDisplayName }, nvencAvailable: true);

        Assert.Equal(ConversionModes.Encode, unavailable.ConversionMode);
        Assert.Equal(ConversionModes.EncodeNvenc, available.ConversionMode);
    }

    private static NvencCapabilityService CreateService(
        string tempPath,
        string smokePath,
        Func<string, IReadOnlyList<string>, ProcessRunResult> runProcess)
    {
        return new NvencCapabilityService(
            runProcess,
            () => smokePath,
            path => File.Exists(path),
            path => File.Exists(path) ? new FileInfo(path).Length : 0,
            path =>
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            });
    }

    private static FfmpegTools CreateTools(string tempPath)
    {
        var ffmpegPath = Path.Combine(tempPath, "ffmpeg.exe");
        var ffprobePath = Path.Combine(tempPath, "ffprobe.exe");
        File.WriteAllText(ffmpegPath, "ffmpeg");
        File.WriteAllText(ffprobePath, "ffprobe");
        return new FfmpegTools(tempPath, ffmpegPath, ffprobePath, FfmpegExists: true, FfprobeExists: true);
    }

    private static bool IsNvidiaSmi(string executable)
    {
        return string.Equals(executable, "nvidia-smi", StringComparison.OrdinalIgnoreCase);
    }

    private static ProcessRunResult Success(string stdout, string stderr = "")
    {
        return new ProcessRunResult(0, false, false, stdout, stderr);
    }

    private static ProcessRunResult Failure(string stderr)
    {
        return new ProcessRunResult(1, false, false, "", stderr);
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
