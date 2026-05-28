namespace DatConverter;

public sealed class SpotterCombinedSplitExportPrototypeResult
{
    public bool Succeeded { get; init; }

    public string InputPath { get; init; } = "";

    public string OutputPath { get; init; } = "";

    public string? CombinedTempH264Path { get; init; }

    public bool KeptTempFile { get; init; }

    public bool TempCleanupSucceeded { get; init; } = true;

    public string FpsValue { get; init; } = "30";

    public OutputFormat OutputFormat { get; init; } = OutputFormat.Mp4;

    public SpotterSplitExportPlan? Plan { get; init; }

    public IReadOnlyList<SpotterDatPayloadExtractionResult> ExtractionResults { get; init; } = Array.Empty<SpotterDatPayloadExtractionResult>();

    public long CombinedTempByteSize { get; init; }

    public IReadOnlyList<string> FfmpegArguments { get; init; } = Array.Empty<string>();

    public ProcessRunResult? FfmpegResult { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public string? FailureReason { get; init; }

    public string BuildTechnicalReport()
    {
        var lines = new List<string>
        {
            $"Combined split export prototype succeeded: {Succeeded}",
            $"Input: {InputPath}",
            $"Output: {OutputPath}",
            $"Output format: {OutputFormat.DisplayName()}",
            $"FPS: {FpsValue}",
            $"Segment count: {Plan?.SegmentCount.ToString() ?? "unknown"}",
            $"Combined temp H.264 path: {CombinedTempH264Path ?? "not kept"}",
            $"Combined temp byte size: {CombinedTempByteSize}",
            $"Temp H.264 kept: {KeptTempFile}",
            $"Temp cleanup succeeded: {TempCleanupSucceeded}"
        };

        if (!string.IsNullOrWhiteSpace(FailureReason))
        {
            lines.Add($"Failure reason: {FailureReason}");
        }

        foreach (var warning in Warnings)
        {
            lines.Add($"Warning: {warning}");
        }

        if (Plan is not null)
        {
            lines.Add("");
            lines.Add("Split export plan");
            lines.Add(Plan.BuildTechnicalReport().Trim());
        }

        if (ExtractionResults.Count > 0)
        {
            lines.Add("");
            lines.Add("Segment extraction reports");
            foreach (var extractionResult in ExtractionResults)
            {
                lines.Add("");
                lines.Add(extractionResult.BuildTechnicalReport().Trim());
            }
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
