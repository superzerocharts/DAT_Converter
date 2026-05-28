namespace DatConverter;

public sealed class SpotterCleanRemuxPrototypeResult
{
    public bool Succeeded { get; init; }

    public string InputPath { get; init; } = "";

    public string OutputPath { get; init; } = "";

    public string? TempH264Path { get; init; }

    public bool KeptTempFile { get; init; }

    public string FpsValue { get; init; } = "30";

    public OutputFormat OutputFormat { get; init; } = OutputFormat.Mp4;

    public SpotterDatPayloadExtractionResult? ExtractionResult { get; init; }

    public IReadOnlyList<string> FfmpegArguments { get; init; } = Array.Empty<string>();

    public ProcessRunResult? FfmpegResult { get; init; }

    public string? FailureReason { get; init; }

    public string BuildTechnicalReport()
    {
        var lines = new List<string>
        {
            $"Clean remux prototype succeeded: {Succeeded}",
            $"Input: {InputPath}",
            $"Output: {OutputPath}",
            $"Output format: {OutputFormat.DisplayName()}",
            $"FPS: {FpsValue}",
            $"Temp H.264 path: {TempH264Path ?? "not created"}",
            $"Temp H.264 kept: {KeptTempFile}"
        };

        if (!string.IsNullOrWhiteSpace(FailureReason))
        {
            lines.Add($"Failure reason: {FailureReason}");
        }

        if (ExtractionResult is not null)
        {
            lines.Add("");
            lines.Add("Extraction report");
            lines.Add(ExtractionResult.BuildTechnicalReport());
        }

        if (FfmpegArguments.Count > 0)
        {
            lines.Add("");
            lines.Add($"FFmpeg arguments: {string.Join(" ", FfmpegArguments.Select(QuoteIfNeeded))}");
        }

        if (FfmpegResult is not null)
        {
            lines.Add($"FFmpeg exit code: {FfmpegResult.ExitCode?.ToString() ?? "none"}");
            lines.Add($"FFmpeg timed out: {FfmpegResult.TimedOut}");
            lines.Add($"FFmpeg canceled: {FfmpegResult.WasCanceled}");
            lines.Add("");
            lines.Add("FFmpeg stdout");
            lines.Add(string.IsNullOrWhiteSpace(FfmpegResult.StandardOutput) ? "(none)" : FfmpegResult.StandardOutput.Trim());
            lines.Add("");
            lines.Add("FFmpeg stderr");
            lines.Add(string.IsNullOrWhiteSpace(FfmpegResult.StandardError) ? "(none)" : FfmpegResult.StandardError.Trim());
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string QuoteIfNeeded(string value)
    {
        return value.Contains(' ', StringComparison.Ordinal) ? $"\"{value}\"" : value;
    }
}
