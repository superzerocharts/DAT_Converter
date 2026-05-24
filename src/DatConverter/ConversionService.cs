using System.Diagnostics;

namespace DatConverter;

public sealed class ConversionService
{
    private static readonly TimeSpan ConversionTimeout = TimeSpan.FromHours(12);

    private readonly FfmpegTools ffmpegTools;

    public ConversionService(FfmpegTools ffmpegTools)
    {
        this.ffmpegTools = ffmpegTools;
    }

    public async Task<ConversionResult> RemuxAsync(
        string inputPath,
        string outputPath,
        OutputFormat outputFormat,
        FpsOption fps,
        TimeSpan? duration,
        IProgress<ConversionProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!HasResolvedFps(fps))
        {
            return BuildUnresolvedFpsResult(inputPath, outputPath, outputFormat, fps, "Fast", duration);
        }

        var arguments = FfmpegCommandBuilder.BuildRemuxArguments(inputPath, outputPath, outputFormat, fps);
        return await RunConversionAsync(
            inputPath,
            outputPath,
            outputFormat,
            fps,
            arguments,
            "Fast",
            "Fast conversion completed.",
            ConversionResult.FastFailedMessage,
            duration,
            progress,
            cancellationToken);
    }

    public async Task<ConversionResult> EncodeAsync(
        string inputPath,
        string outputPath,
        OutputFormat outputFormat,
        FpsOption fps,
        TimeSpan? duration,
        IProgress<ConversionProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!HasResolvedFps(fps))
        {
            return BuildUnresolvedFpsResult(inputPath, outputPath, outputFormat, fps, "Encode", duration);
        }

        var arguments = FfmpegCommandBuilder.BuildEncodeArguments(inputPath, outputPath, outputFormat, fps);
        return await RunConversionAsync(
            inputPath,
            outputPath,
            outputFormat,
            fps,
            arguments,
            "Encode",
            "Full conversion completed.",
            ConversionResult.FullFailedMessage,
            duration,
            progress,
            cancellationToken);
    }

    private async Task<ConversionResult> RunConversionAsync(
        string inputPath,
        string outputPath,
        OutputFormat outputFormat,
        FpsOption fps,
        IReadOnlyList<string> arguments,
        string conversionMode,
        string successMessage,
        string failureMessage,
        TimeSpan? duration,
        IProgress<ConversionProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!OutputPathService.IsSafeOutputPath(inputPath, outputPath))
        {
            return new ConversionResult(
                false,
                "Conversion blocked because the output path matches the source .dat file.",
                ffmpegTools.FfmpegPath,
                arguments,
                inputPath,
                outputPath,
                fps,
                null,
                "",
                "Output path safety guard blocked conversion before FFmpeg started.",
                ConversionMode: conversionMode,
                OutputFormat: outputFormat.DisplayName(),
                Duration: duration,
                UsedDeterminateProgress: duration.HasValue);
        }

        if (File.Exists(outputPath))
        {
            return new ConversionResult(
                false,
                "Conversion blocked because the output file already exists.",
                ffmpegTools.FfmpegPath,
                arguments,
                inputPath,
                outputPath,
                fps,
                null,
                "",
                "Output path collision guard blocked conversion before FFmpeg started.",
                ConversionMode: conversionMode,
                OutputFormat: outputFormat.DisplayName(),
                Duration: duration,
                UsedDeterminateProgress: duration.HasValue);
        }

        var hadSidecarPartialBeforeConversion = File.Exists(outputPath + ".partial");
        var progressParser = new ConversionProgressParser(duration);
        var stopwatch = Stopwatch.StartNew();
        var processResult = await FfmpegProcessRunner.RunAsync(
            ffmpegTools.FfmpegPath,
            arguments,
            ConversionTimeout,
            cancellationToken,
            line =>
            {
                var progressUpdate = progressParser.ParseLine(line);
                if (progressUpdate is not null)
                {
                    progress?.Report(progressUpdate);
                }
            });
        stopwatch.Stop();
        var processingTime = stopwatch.Elapsed;

        if (processResult.ExitCode == 0 && TryGetFileLength(outputPath) > 0)
        {
            return new ConversionResult(
                true,
                successMessage,
                ffmpegTools.FfmpegPath,
                arguments,
                inputPath,
                outputPath,
                fps,
                processResult.ExitCode,
                processResult.StandardOutput,
                processResult.StandardError,
                ConversionMode: conversionMode,
                OutputFormat: outputFormat.DisplayName(),
                TimedOut: processResult.TimedOut,
                Duration: duration,
                UsedDeterminateProgress: duration.HasValue,
                ProcessingTime: processingTime);
        }

        var partialOutputMessage = processResult.WasCanceled
            ? PartialOutputService.TryDeleteCanceledOutput(outputPath, inputPath, deleteSidecarPartial: !hadSidecarPartialBeforeConversion)
            : PartialOutputService.TryMovePartialOutput(outputPath, inputPath);
        var userMessage = processResult.WasCanceled ? ConversionResult.CanceledMessage : failureMessage;
        return new ConversionResult(
            false,
            userMessage,
            ffmpegTools.FfmpegPath,
            arguments,
            inputPath,
            outputPath,
            fps,
            processResult.ExitCode,
            processResult.StandardOutput,
            processResult.StandardError,
            partialOutputMessage,
            conversionMode,
            outputFormat.DisplayName(),
            processResult.WasCanceled,
            processResult.TimedOut,
            duration,
            duration.HasValue,
            processingTime);
    }

    private static long TryGetFileLength(string path)
    {
        try
        {
            return File.Exists(path) ? new FileInfo(path).Length : 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or PathTooLongException or NotSupportedException)
        {
            return 0;
        }
    }

    private static bool HasResolvedFps(FpsOption fps)
    {
        return !string.IsNullOrWhiteSpace(fps.FfmpegValue);
    }

    private ConversionResult BuildUnresolvedFpsResult(
        string inputPath,
        string outputPath,
        OutputFormat outputFormat,
        FpsOption fps,
        string conversionMode,
        TimeSpan? duration)
    {
        return new ConversionResult(
            false,
            "Conversion blocked because Source FPS is not set.",
            ffmpegTools.FfmpegPath,
            Array.Empty<string>(),
            inputPath,
            outputPath,
            fps,
            null,
            "",
            "Source FPS is not set. Choose Source FPS before converting.",
            ConversionMode: conversionMode,
            OutputFormat: outputFormat.DisplayName(),
            Duration: duration,
            UsedDeterminateProgress: duration.HasValue);
    }
}
