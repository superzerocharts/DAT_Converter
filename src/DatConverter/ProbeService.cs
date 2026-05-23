using System.Text.Json;

namespace DatConverter;

public sealed class ProbeService
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(8);

    private readonly FfmpegTools ffmpegTools;

    public ProbeService(FfmpegTools ffmpegTools)
    {
        this.ffmpegTools = ffmpegTools;
    }

    public async Task<ProbeResult> ProbeRawH264Async(string inputFilePath, FpsOption fps, CancellationToken cancellationToken)
    {
        var ffprobeArguments = new[]
        {
            "-v", "error",
            "-f", "h264",
            "-framerate", fps.FfmpegValue,
            "-i", inputFilePath,
            "-select_streams", "v:0",
            "-show_entries", "stream=codec_name,profile,width,height,pix_fmt,r_frame_rate,avg_frame_rate,duration",
            "-show_entries", "format=duration",
            "-of", "json"
        };

        var ffprobeResult = await FfmpegProcessRunner.RunAsync(ffmpegTools.FfprobePath, ffprobeArguments, ProbeTimeout, cancellationToken);
        if (ffprobeResult.WasCanceled)
        {
            return CreateFailure(ffmpegTools.FfprobePath, fps, "Probe was canceled.");
        }

        if (ffprobeResult.ExitCode == 0)
        {
            var parsedResult = TryParseFfprobeJson(ffprobeResult.StandardOutput, ffmpegTools.FfprobePath, fps, ffprobeResult.StandardError);
            if (parsedResult.IsSuccess)
            {
                return parsedResult;
            }
        }

        var ffmpegFallbackResult = await RunShortFfmpegValidationAsync(inputFilePath, fps, cancellationToken);
        if (ffmpegFallbackResult.IsSuccess)
        {
            return ffmpegFallbackResult;
        }

        return CreateFailure(
            ffmpegTools.FfprobePath,
            fps,
            BuildTechnicalDetails("ffprobe", ffprobeResult, "ffmpeg fallback", ffmpegFallbackResult.TechnicalDetails));
    }

    private async Task<ProbeResult> RunShortFfmpegValidationAsync(string inputFilePath, FpsOption fps, CancellationToken cancellationToken)
    {
        var ffmpegArguments = new[]
        {
            "-v", "error",
            "-f", "h264",
            "-r", fps.FfmpegValue,
            "-i", inputFilePath,
            "-frames:v", "5",
            "-f", "null",
            "NUL"
        };

        var result = await FfmpegProcessRunner.RunAsync(ffmpegTools.FfmpegPath, ffmpegArguments, ProbeTimeout, cancellationToken);
        if (result.WasCanceled)
        {
            return CreateFailure(ffmpegTools.FfmpegPath, fps, "Probe was canceled.");
        }

        if (result.ExitCode == 0)
        {
            return new ProbeResult(
                true,
                "Probe succeeded using a short FFmpeg raw H.264 validation pass.",
                ffmpegTools.FfmpegPath,
                fps,
                CodecName: "h264",
                TechnicalDetails: BuildTechnicalDetails("ffmpeg fallback", result));
        }

        return CreateFailure(ffmpegTools.FfmpegPath, fps, BuildTechnicalDetails("ffmpeg fallback", result));
    }

    private static ProbeResult TryParseFfprobeJson(string json, string toolPath, FpsOption fps, string standardError)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (!root.TryGetProperty("streams", out var streams) || streams.ValueKind != JsonValueKind.Array || streams.GetArrayLength() == 0)
            {
                return CreateFailure(toolPath, fps, "ffprobe did not report a video stream.");
            }

            var stream = streams[0];
            var codecName = GetString(stream, "codec_name");
            var width = GetInt(stream, "width");
            var height = GetInt(stream, "height");

            if (!string.Equals(codecName, "h264", StringComparison.OrdinalIgnoreCase))
            {
                return CreateFailure(toolPath, fps, $"ffprobe reported codec '{codecName ?? "unknown"}', not h264.");
            }

            if ((width.HasValue && width.Value <= 0) || (height.HasValue && height.Value <= 0))
            {
                return CreateFailure(toolPath, fps, "ffprobe reported an invalid video resolution.");
            }

            var formatDuration = root.TryGetProperty("format", out var format) ? GetString(format, "duration") : null;
            var streamDuration = GetString(stream, "duration");

            return new ProbeResult(
                true,
                "Probe succeeded. Raw H.264 video stream detected.",
                toolPath,
                fps,
                codecName,
                GetString(stream, "profile"),
                width,
                height,
                GetString(stream, "pix_fmt"),
                GetString(stream, "r_frame_rate"),
                GetString(stream, "avg_frame_rate"),
                FirstUsefulValue(formatDuration, streamDuration),
                string.IsNullOrWhiteSpace(standardError) ? "ffprobe completed without stderr output." : standardError);
        }
        catch (JsonException ex)
        {
            return CreateFailure(toolPath, fps, $"Could not parse ffprobe JSON output: {ex.Message}");
        }
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind != JsonValueKind.Null
            ? property.ToString()
            : null;
    }

    private static int? GetInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.TryGetInt32(out var value) ? value : null;
    }

    private static string? FirstUsefulValue(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value) && value != "N/A")
            {
                return value;
            }
        }

        return null;
    }

    private static ProbeResult CreateFailure(string toolPath, FpsOption fps, string technicalDetails)
    {
        return new ProbeResult(false, ProbeResult.UnsupportedMessage, toolPath, fps, TechnicalDetails: technicalDetails);
    }

    private static string BuildTechnicalDetails(string label, ProcessRunResult result, string? additionalLabel = null, string? additionalDetails = null)
    {
        var lines = new List<string>
        {
            $"{label} exit code: {FormatExitCode(result.ExitCode)}",
            $"{label} timed out: {result.TimedOut}",
            $"{label} stdout: {FormatProcessText(result.StandardOutput)}",
            $"{label} stderr: {FormatProcessText(result.StandardError)}"
        };

        if (!string.IsNullOrWhiteSpace(additionalLabel) && !string.IsNullOrWhiteSpace(additionalDetails))
        {
            lines.Add($"{additionalLabel} details: {additionalDetails}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string FormatExitCode(int? exitCode)
    {
        return exitCode?.ToString() ?? "none";
    }

    private static string FormatProcessText(string text)
    {
        return string.IsNullOrWhiteSpace(text) ? "(none)" : text.Trim();
    }
}
