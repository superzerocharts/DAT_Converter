namespace DatConverter.Tests;

public sealed class ConversionTelemetryTests
{
    [Fact]
    public async Task EncodeAsync_SuccessCapturesStructuredTelemetry()
    {
        using var temp = new TempDirectory();
        var inputPath = Path.Combine(temp.Path, "clip.dat");
        var outputPath = Path.Combine(temp.Path, "clip.mp4");
        File.WriteAllBytes(inputPath, new byte[1000]);
        var service = CreateService((_, _, _, _, onOutput, _) =>
        {
            onOutput?.Invoke("frame=90");
            onOutput?.Invoke("fps=45.5");
            onOutput?.Invoke("bitrate=800.0kbits/s");
            onOutput?.Invoke("total_size=500");
            onOutput?.Invoke("out_time_us=3000000");
            onOutput?.Invoke("dup_frames=4");
            onOutput?.Invoke("drop_frames=2");
            onOutput?.Invoke("speed=1.5x");
            onOutput?.Invoke("progress=end");
            File.WriteAllBytes(outputPath, new byte[500]);
            return Task.FromResult(new ProcessRunResult(
                0,
                false,
                false,
                "",
                """
                ffmpeg version 7.1-full_build
                configuration: --enable-gpl --enable-libx264
                [libx264 @ 000001] using cpu capabilities: MMX2 SSE2Fast
                [libx264 @ 000001] frame I:1 Avg QP:20.00 size:1000
                [libx264 @ 000001] kb/s:800.00
                """));
        });

        var result = await service.EncodeAsync(
            inputPath,
            outputPath,
            OutputFormat.Mp4,
            FpsOption.FromLabel("30"),
            TimeSpan.FromSeconds(3),
            null,
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.StandardError);
        Assert.NotNull(result.Telemetry);
        Assert.Equal(1000, result.Telemetry.InputFileSizeBytes);
        Assert.Equal(500, result.Telemetry.OutputFileSizeBytes);
        Assert.Equal(0.5, result.Telemetry.CompressionRatio);
        Assert.Equal("MP4", result.Telemetry.OutputContainer);
        Assert.Equal("Encode", result.Telemetry.ConversionMode);
        Assert.Equal("Full", result.Telemetry.ModeLabel);
        Assert.Equal("libx264", result.Telemetry.EncoderFamily);
        Assert.Equal("veryfast", result.Telemetry.EncoderPreset);
        Assert.Equal("CRF", result.Telemetry.QualityMode);
        Assert.Equal("22", result.Telemetry.QualityValue);
        Assert.False(result.Telemetry.TrimUsed);
        Assert.False(result.Telemetry.BurnTimestampUsed);
        Assert.Equal("30", result.Telemetry.SelectedFpsLabel);
        Assert.Equal("30", result.Telemetry.SelectedFfmpegFpsValue);
        Assert.True(result.Telemetry.DurationAvailable);
        Assert.Equal(3, result.Telemetry.DurationSeconds);
        Assert.Equal(0, result.Telemetry.ExitCode);
        Assert.True(result.Telemetry.Succeeded);
        Assert.False(result.Telemetry.Canceled);
        Assert.False(result.Telemetry.Failed);
        Assert.Equal("1.5x", result.Telemetry.FinalReportedSpeed);
        Assert.Equal("45.5", result.Telemetry.FinalReportedFps);
        Assert.Equal("90", result.Telemetry.FinalReportedFrame);
        Assert.Equal("800.0kbits/s", result.Telemetry.ReportedBitrate);
        Assert.Equal("500", result.Telemetry.FinalReportedTotalSize);
        Assert.Equal("4", result.Telemetry.FinalReportedDupFrames);
        Assert.Equal("2", result.Telemetry.FinalReportedDropFrames);
        Assert.Equal("3000000", result.Telemetry.FinalReportedOutTimeUs);
        Assert.Equal(TimeSpan.FromSeconds(3), result.Telemetry.FinalOutputTime);
        Assert.Equal("ffmpeg version 7.1-full_build", result.Telemetry.FfmpegVersionLine);
        Assert.Equal("configuration: --enable-gpl --enable-libx264", result.Telemetry.FfmpegConfigurationLine);
        Assert.Contains("using cpu capabilities", result.Telemetry.Libx264VersionLine);
        Assert.Contains(result.Telemetry.X264SummaryLines!, line => line.Contains("frame I:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EncodeAsync_TelemetryAllowsMissingOptionalValues()
    {
        using var temp = new TempDirectory();
        var inputPath = Path.Combine(temp.Path, "clip.dat");
        var outputPath = Path.Combine(temp.Path, "clip.mkv");
        File.WriteAllBytes(inputPath, new byte[1000]);
        var service = CreateService((_, _, _, _, _, _) =>
        {
            File.WriteAllBytes(outputPath, new byte[250]);
            return Task.FromResult(new ProcessRunResult(0, false, false, "", ""));
        });

        var result = await service.EncodeAsync(
            inputPath,
            outputPath,
            OutputFormat.Mkv,
            FpsOption.FromLabel("29.97"),
            null,
            null,
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.StandardError);
        Assert.NotNull(result.Telemetry);
        Assert.Equal(1000, result.Telemetry.InputFileSizeBytes);
        Assert.Equal(250, result.Telemetry.OutputFileSizeBytes);
        Assert.Equal(0.25, result.Telemetry.CompressionRatio);
        Assert.Equal("MKV", result.Telemetry.OutputContainer);
        Assert.False(result.Telemetry.DurationAvailable);
        Assert.Null(result.Telemetry.DurationSeconds);
        Assert.Null(result.Telemetry.AverageEncodeSpeed);
        Assert.Null(result.Telemetry.FinalReportedFps);
        Assert.Null(result.Telemetry.ReportedBitrate);
        Assert.Null(result.Telemetry.FfmpegVersionLine);
        Assert.Equal("29.97", result.Telemetry.SelectedFpsLabel);
        Assert.Equal("30000/1001", result.Telemetry.SelectedFfmpegFpsValue);
    }

    [Fact]
    public async Task EncodeNvencAsync_TelemetryIdentifiesNvencSettings()
    {
        using var temp = new TempDirectory();
        var inputPath = Path.Combine(temp.Path, "clip.dat");
        var outputPath = Path.Combine(temp.Path, "clip.mp4");
        File.WriteAllBytes(inputPath, new byte[1000]);
        var service = CreateService((_, arguments, _, _, onOutput, _) =>
        {
            Assert.Contains("h264_nvenc", arguments);
            onOutput?.Invoke("frame=60");
            onOutput?.Invoke("fps=120");
            onOutput?.Invoke("speed=4.0x");
            onOutput?.Invoke("progress=end");
            File.WriteAllBytes(outputPath, new byte[300]);
            return Task.FromResult(new ProcessRunResult(0, false, false, "", ""));
        });

        var result = await service.EncodeNvencAsync(
            inputPath,
            outputPath,
            OutputFormat.Mp4,
            FpsOption.FromLabel("30"),
            TimeSpan.FromSeconds(2),
            null,
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.StandardError);
        Assert.NotNull(result.Telemetry);
        Assert.Equal(ConversionModes.EncodeNvenc, result.Telemetry.ConversionMode);
        Assert.Equal("Full NVENC", result.Telemetry.ModeLabel);
        Assert.Equal("h264_nvenc", result.Telemetry.EncoderFamily);
        Assert.Equal("p1", result.Telemetry.EncoderPreset);
        Assert.Equal("CQ", result.Telemetry.QualityMode);
        Assert.Equal("23", result.Telemetry.QualityValue);
        Assert.Equal("120", result.Telemetry.FinalReportedFps);
        Assert.Equal("4.0x", result.Telemetry.FinalReportedSpeed);
    }

    private static ConversionService CreateService(
        Func<string, IReadOnlyList<string>, TimeSpan, CancellationToken, Action<string>?, Action<string>?, Task<ProcessRunResult>> runProcessAsync)
    {
        return new ConversionService(
            new FfmpegTools(AppContext.BaseDirectory, "ffmpeg.exe", "ffprobe.exe", FfmpegExists: true, FfprobeExists: true),
            InternalConversionPathOptions.Default,
            (_, _, _) => throw new InvalidOperationException("Clean extractor should not run."),
            (_, _, _, _, _, _) => throw new InvalidOperationException("Trim extractor should not run."),
            runProcessAsync);
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
