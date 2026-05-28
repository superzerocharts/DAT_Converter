using System.Globalization;

namespace DatConverter;

public sealed record ConversionTelemetry(
    long? InputFileSizeBytes = null,
    long? OutputFileSizeBytes = null,
    double? CompressionRatio = null,
    string? ReportedBitrate = null,
    double? ComputedOutputBitrateKbps = null,
    string? OutputContainer = null,
    string? ConversionMode = null,
    string? ModeLabel = null,
    string? EncoderFamily = null,
    string? EncoderPreset = null,
    string? QualityMode = null,
    string? QualityValue = null,
    bool? NvencAvailable = null,
    bool? TrimUsed = null,
    bool? BurnTimestampUsed = null,
    bool? SplitExportUsed = null,
    string? Codec = null,
    string? Profile = null,
    int? Width = null,
    int? Height = null,
    string? PixelFormat = null,
    string? SelectedFpsLabel = null,
    string? SelectedFfmpegFpsValue = null,
    bool? DurationAvailable = null,
    double? DurationSeconds = null,
    TimeSpan? ElapsedConversionTime = null,
    int? ExitCode = null,
    bool? Succeeded = null,
    bool? Canceled = null,
    bool? Failed = null,
    double? AverageEncodeSpeed = null,
    string? FinalReportedSpeed = null,
    string? FinalReportedFps = null,
    string? FinalReportedFrame = null,
    string? FinalReportedTotalSize = null,
    string? FinalReportedDupFrames = null,
    string? FinalReportedDropFrames = null,
    string? FinalReportedOutTimeUs = null,
    string? FinalReportedOutTimeMs = null,
    TimeSpan? FinalOutputTime = null,
    string? FfmpegVersionLine = null,
    string? FfmpegConfigurationLine = null,
    string? Libx264VersionLine = null,
    IReadOnlyList<string>? X264SummaryLines = null)
{
    public static ConversionTelemetry Build(
        string inputPath,
        string outputPath,
        OutputFormat outputFormat,
        string conversionMode,
        FpsOption fps,
        TimeSpan? duration,
        TimeSpan? elapsed,
        int? exitCode,
        bool succeeded,
        bool canceled,
        string standardError,
        ConversionProgress? finalProgress,
        ConversionInputPathMode inputPathMode,
        IReadOnlyList<string> arguments)
    {
        var inputSize = TryGetFileLength(inputPath);
        var outputSize = TryGetFileLength(outputPath);
        if (!succeeded)
        {
            outputSize = null;
        }

        double? durationSeconds = duration.HasValue && duration.Value > TimeSpan.Zero
            ? duration.Value.TotalSeconds
            : null;
        double? outputSeconds = finalProgress?.OutputTime is { } outputTime && outputTime > TimeSpan.Zero
            ? outputTime.TotalSeconds
            : null;
        var bitrateDurationSeconds = durationSeconds ?? outputSeconds;
        double? computedBitrate = outputSize.HasValue && bitrateDurationSeconds.HasValue && bitrateDurationSeconds.Value > 0
            ? outputSize.Value * 8D / bitrateDurationSeconds.Value / 1000D
            : null;

        double? elapsedSeconds = elapsed.HasValue && elapsed.Value > TimeSpan.Zero
            ? elapsed.Value.TotalSeconds
            : null;
        double? averageEncodeSpeed = elapsedSeconds.HasValue && bitrateDurationSeconds.HasValue && bitrateDurationSeconds.Value > 0
            ? bitrateDurationSeconds.Value / elapsedSeconds.Value
            : null;

        var buildInfo = ParseBuildInfo(standardError);
        var encoderInfo = ParseEncoderInfo(arguments);
        return new ConversionTelemetry(
            InputFileSizeBytes: inputSize,
            OutputFileSizeBytes: outputSize,
            CompressionRatio: inputSize.HasValue && inputSize.Value > 0 && outputSize.HasValue
                ? outputSize.Value / (double)inputSize.Value
                : null,
            ReportedBitrate: Normalize(finalProgress?.Bitrate),
            ComputedOutputBitrateKbps: computedBitrate,
            OutputContainer: outputFormat.DisplayName(),
            ConversionMode: conversionMode,
            ModeLabel: ConversionModes.FormatDisplay(conversionMode),
            EncoderFamily: encoderInfo.EncoderFamily,
            EncoderPreset: encoderInfo.EncoderPreset,
            QualityMode: encoderInfo.QualityMode,
            QualityValue: encoderInfo.QualityValue,
            TrimUsed: inputPathMode == ConversionInputPathMode.TrimmedCleanH264,
            BurnTimestampUsed: arguments.Any(argument => argument.Contains("drawtext", StringComparison.OrdinalIgnoreCase)),
            SplitExportUsed: null,
            SelectedFpsLabel: fps.Label,
            SelectedFfmpegFpsValue: fps.FfmpegValue,
            DurationAvailable: durationSeconds.HasValue,
            DurationSeconds: durationSeconds,
            ElapsedConversionTime: elapsed,
            ExitCode: exitCode,
            Succeeded: succeeded,
            Canceled: canceled,
            Failed: !succeeded && !canceled,
            AverageEncodeSpeed: averageEncodeSpeed,
            FinalReportedSpeed: Normalize(finalProgress?.Speed),
            FinalReportedFps: Normalize(finalProgress?.Fps),
            FinalReportedFrame: Normalize(finalProgress?.Frame),
            FinalReportedTotalSize: Normalize(finalProgress?.TotalSize),
            FinalReportedDupFrames: Normalize(finalProgress?.DupFrames),
            FinalReportedDropFrames: Normalize(finalProgress?.DropFrames),
            FinalReportedOutTimeUs: Normalize(finalProgress?.OutTimeUs),
            FinalReportedOutTimeMs: Normalize(finalProgress?.OutTimeMs),
            FinalOutputTime: finalProgress?.OutputTime,
            FfmpegVersionLine: buildInfo.FfmpegVersionLine,
            FfmpegConfigurationLine: buildInfo.FfmpegConfigurationLine,
            Libx264VersionLine: buildInfo.Libx264VersionLine,
            X264SummaryLines: buildInfo.X264SummaryLines);
    }

    public ConversionTelemetry WithProbe(ProbeResult? probe)
    {
        if (probe is null)
        {
            return this;
        }

        return this with
        {
            Codec = probe.CodecName,
            Profile = probe.Profile,
            Width = probe.Width,
            Height = probe.Height,
            PixelFormat = probe.PixelFormat
        };
    }

    public ConversionTelemetry WithPathFlags(bool? trimUsed = null, bool? burnTimestampUsed = null, bool? splitExportUsed = null)
    {
        return this with
        {
            TrimUsed = trimUsed ?? TrimUsed,
            BurnTimestampUsed = burnTimestampUsed ?? BurnTimestampUsed,
            SplitExportUsed = splitExportUsed ?? SplitExportUsed
        };
    }

    public ConversionTelemetry WithNvencAvailability(bool? nvencAvailable)
    {
        return this with { NvencAvailable = nvencAvailable ?? NvencAvailable };
    }

    private static long? TryGetFileLength(string path)
    {
        try
        {
            return File.Exists(path) ? new FileInfo(path).Length : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or PathTooLongException or NotSupportedException)
        {
            return null;
        }
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) || string.Equals(value, "N/A", StringComparison.OrdinalIgnoreCase)
            ? null
            : value.Trim();
    }

    private static EncoderInfo ParseEncoderInfo(IReadOnlyList<string> arguments)
    {
        var codec = GetOptionValue(arguments, "-c:v");
        if (string.Equals(codec, "h264_nvenc", StringComparison.OrdinalIgnoreCase))
        {
            return new EncoderInfo("h264_nvenc", GetOptionValue(arguments, "-preset"), "CQ", GetOptionValue(arguments, "-cq"));
        }

        if (string.Equals(codec, "libx264", StringComparison.OrdinalIgnoreCase))
        {
            return new EncoderInfo("libx264", GetOptionValue(arguments, "-preset"), "CRF", GetOptionValue(arguments, "-crf"));
        }

        return new EncoderInfo(Normalize(codec), GetOptionValue(arguments, "-preset"), null, null);
    }

    private static string? GetOptionValue(IReadOnlyList<string> arguments, string option)
    {
        for (var index = 0; index < arguments.Count - 1; index++)
        {
            if (string.Equals(arguments[index], option, StringComparison.OrdinalIgnoreCase))
            {
                return Normalize(arguments[index + 1]);
            }
        }

        return null;
    }

    private static BuildInfo ParseBuildInfo(string standardError)
    {
        if (string.IsNullOrWhiteSpace(standardError))
        {
            return new BuildInfo(null, null, null, Array.Empty<string>());
        }

        var lines = standardError
            .Split(["\r\n", "\n"], StringSplitOptions.None)
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();

        var version = lines.FirstOrDefault(line => line.StartsWith("ffmpeg version ", StringComparison.OrdinalIgnoreCase));
        var configuration = lines.FirstOrDefault(line => line.StartsWith("configuration:", StringComparison.OrdinalIgnoreCase));
        var libx264Version = lines.FirstOrDefault(line =>
            line.Contains("libx264", StringComparison.OrdinalIgnoreCase) &&
            (line.Contains("core", StringComparison.OrdinalIgnoreCase) ||
             line.Contains("using cpu capabilities", StringComparison.OrdinalIgnoreCase) ||
             line.Contains("profile ", StringComparison.OrdinalIgnoreCase)));
        var summary = lines
            .Where(IsX264SummaryLine)
            .Take(20)
            .ToArray();
        return new BuildInfo(version, configuration, libx264Version, summary);
    }

    private static bool IsX264SummaryLine(string line)
    {
        if (!line.Contains("libx264", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return line.Contains("frame I:", StringComparison.OrdinalIgnoreCase) ||
               line.Contains("frame P:", StringComparison.OrdinalIgnoreCase) ||
               line.Contains("frame B:", StringComparison.OrdinalIgnoreCase) ||
               line.Contains("kb/s", StringComparison.OrdinalIgnoreCase) ||
               line.Contains("encoded", StringComparison.OrdinalIgnoreCase) ||
               line.Contains("consecutive B-frames", StringComparison.OrdinalIgnoreCase) ||
               line.Contains("Weighted P-Frames", StringComparison.OrdinalIgnoreCase);
    }

    public static string FormatSeconds(double? value)
    {
        return value.HasValue
            ? value.Value.ToString("0.###", CultureInfo.InvariantCulture)
            : "Unknown";
    }

    public static string FormatRatio(double? value)
    {
        return value.HasValue
            ? value.Value.ToString("0.###", CultureInfo.InvariantCulture)
            : "Unknown";
    }

    private sealed record BuildInfo(
        string? FfmpegVersionLine,
        string? FfmpegConfigurationLine,
        string? Libx264VersionLine,
        IReadOnlyList<string> X264SummaryLines);

    private sealed record EncoderInfo(
        string? EncoderFamily,
        string? EncoderPreset,
        string? QualityMode,
        string? QualityValue);
}
