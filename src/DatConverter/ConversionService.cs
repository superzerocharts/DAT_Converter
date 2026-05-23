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
        var arguments = FfmpegCommandBuilder.BuildEncodeArguments(inputPath, outputPath, outputFormat, fps);
        return await RunConversionAsync(
            inputPath,
            outputPath,
            outputFormat,
            fps,
            arguments,
            "Full",
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

        var progressParser = new ConversionProgressParser(duration);
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
                UsedDeterminateProgress: duration.HasValue);
        }

        var partialOutputMessage = PartialOutputService.TryMovePartialOutput(outputPath, inputPath);
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
            duration.HasValue);
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
}
