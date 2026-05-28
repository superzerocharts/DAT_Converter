namespace DatConverter;

public sealed class SpotterFpsDetectionResult
{
    public bool Succeeded { get; init; }
    public string FailureReason { get; init; } = "";
    public string DetectionSource { get; init; } = "";
    public string Confidence { get; init; } = "Low";
    public SpotterFpsTechnicalDetails TechnicalDetails { get; init; } = new();

    public string BuildTechnicalLogText()
    {
        var details = TechnicalDetails;
        var lines = new List<string>
        {
            $"Spotter FPS detection succeeded: {Succeeded}",
            $"Detection source: {FormatValue(DetectionSource)}",
            $"Confidence: {Confidence}"
        };

        if (!Succeeded)
        {
            lines.Add($"Failure reason: {FormatValue(FailureReason)}");
        }

        lines.Add($"Records: {details.FrameCount} ({details.H264KeyframeCount} H264 keyframes, {details.I264InterframeCount} I264 interframes)");
        lines.Add($"Resolution: {FormatResolution(details.Width, details.Height)}");
        lines.Add($"Multiple resolutions detected: {details.MultipleResolutionsDetected}");
        lines.Add($"First timestamp: {details.FirstTimestamp?.ToString() ?? "unknown"}");
        lines.Add($"Last timestamp: {details.LastTimestamp?.ToString() ?? "unknown"}");
        lines.Add($"Timestamp span: {details.StreamTimestampSpan?.ToString() ?? "unknown"}");
        lines.Add($"Timebase: {FormatDouble(details.TimebaseUnitsPerSecond, "0.######")} units/sec");
        lines.Add($"Duration: {FormatDouble(details.DurationSeconds, "0.###")} sec");
        lines.Add($"Average FPS: {FormatDouble(details.AverageFps, "0.###")}");
        lines.Add($"Instant FPS: median={FormatDouble(details.InstantFpsMedian, "0.###")}, min={FormatDouble(details.InstantFpsMin, "0.###")}, max={FormatDouble(details.InstantFpsMax, "0.###")}");
        lines.Add($"Per-second FPS: median={FormatDouble(details.BucketMedianFps, "0.###")}, mode={details.BucketModeFps?.ToString() ?? "unknown"}, min={details.BucketMinFps?.ToString() ?? "unknown"}, max={details.BucketMaxFps?.ToString() ?? "unknown"}, buckets={details.BucketCount}");

        foreach (var warning in details.Warnings)
        {
            lines.Add($"Warning: {warning}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string FormatResolution(int? width, int? height)
    {
        return width.HasValue && height.HasValue ? $"{width}x{height}" : "unknown";
    }

    private static string FormatDouble(double? value, string format)
    {
        return value.HasValue
            ? value.Value.ToString(format, System.Globalization.CultureInfo.InvariantCulture)
            : "unknown";
    }

    private static string FormatValue(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "unknown" : value;
    }
}
