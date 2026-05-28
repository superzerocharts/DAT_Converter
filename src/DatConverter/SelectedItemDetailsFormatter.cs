namespace DatConverter;

public static class SelectedItemDetailsFormatter
{
    public static List<string> BuildLines(QueueItem? item)
    {
        if (item is null)
        {
            return BuildBlankLines();
        }

        var duration = ParseProbeDuration(item.PreProbeResult?.Duration);
        var result = item.ConversionResult;

        return new List<string>
        {
            FormatField("Input file", item.InputPath),
            FormatField("Planned output file", item.PlannedOutputPath),
            FormatField("Output format", item.OutputFormat.DisplayName()),
            FormatField("Mode", FormatConversionModeForDisplay(item.ConversionMode)),
            FormatField("Selected source FPS", FormatFpsDisplay(item)),
            FormatField("FFmpeg FPS value", FormatFfmpegRateValue(item)),
            FormatField("FPS confidence", FormatFpsConfidence(item)),
            FormatField("FPS note", FormatFpsNote(item)),
            FormatField("Source type", FormatSourceType(item)),
            FormatField("Parts", FormatParts(item)),
            FormatField("Export segment", FormatExportSegment(item)),
            FormatField("Split recording segments", FormatSplitSegments(item)),
            FormatField("Trim", TrimRangeFormatter.FormatTrimState(item)),
            FormatField("Probe status", FormatProbeStatus(item)),
            FormatField("Conversion status", FormatConversionStatus(item)),
            FormatField("Duration available", FormatYesNo(duration.HasValue)),
            FormatField("Duration value", FormatDuration(duration)),
            FormatField("Progress mode", duration.HasValue ? "Determinate" : "Indeterminate"),
            FormatField("Status", QueueItemResultFormatter.GetStatusSummary(item)),
            FormatField("Output", QueueItemResultFormatter.GetOutputSummary(item)),
            FormatField("Exit code", FormatOptionalExitCode(result?.ExitCode)),
            FormatField("Canceled", FormatYesNo(result?.WasCanceled == true)),
            FormatField("Timed out", FormatYesNo(result?.TimedOut == true))
        };
    }

    private static List<string> BuildBlankLines()
    {
        return new List<string>
        {
            "Input file:",
            "Planned output file:",
            "Output format:",
            "Mode:",
            "Selected source FPS:",
            "FFmpeg FPS value:",
            "FPS confidence:",
            "FPS note:",
            "Source type:",
            "Parts:",
            "Export segment:",
            "Split recording segments:",
            "Trim:",
            "Probe status:",
            "Conversion status:",
            "Duration available:",
            "Duration value:",
            "Progress mode:",
            "Status:",
            "Output:",
            "Exit code:",
            "Canceled:",
            "Timed out:"
        };
    }

    private static string FormatProbeStatus(QueueItem item)
    {
        if (item.Status == QueueItemStatus.Probing)
        {
            return "Running";
        }

        return item.PreProbeResult is null
            ? "Not validated"
            : item.PreProbeResult.IsSuccess ? "Succeeded" : "Failed";
    }

    private static string FormatConversionStatus(QueueItem item)
    {
        return item.Status switch
        {
            QueueItemStatus.Converting => "Running",
            QueueItemStatus.Completed => "Completed",
            QueueItemStatus.Canceled => "Canceled",
            QueueItemStatus.Failed or QueueItemStatus.Invalid => "Failed",
            QueueItemStatus.Unsupported => "Skipped",
            QueueItemStatus.Skipped => "Skipped",
            _ => "Not started"
        };
    }

    private static TimeSpan? ParseProbeDuration(string? duration)
    {
        if (string.IsNullOrWhiteSpace(duration) || string.Equals(duration, "N/A", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (double.TryParse(duration, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var seconds) && seconds > 0)
        {
            return TimeSpan.FromSeconds(seconds);
        }

        return TimeSpan.TryParse(duration, System.Globalization.CultureInfo.InvariantCulture, out var parsed) && parsed > TimeSpan.Zero
            ? parsed
            : null;
    }

    private static string FormatDuration(TimeSpan? duration)
    {
        if (!duration.HasValue)
        {
            return "Unknown";
        }

        return duration.Value.TotalHours >= 1
            ? duration.Value.ToString(@"h\:mm\:ss", System.Globalization.CultureInfo.InvariantCulture)
            : duration.Value.ToString(@"mm\:ss", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string FormatConversionModeForDisplay(string? conversionMode)
    {
        return string.Equals(conversionMode, "Encode", StringComparison.OrdinalIgnoreCase)
            ? "Full"
            : "Fast";
    }

    private static string FormatFpsNote(QueueItem item)
    {
        if (item.RequiresManualFpsSelection || !item.HasResolvedFps)
        {
            return item.FpsValidationMessage ?? "Auto-detect could not determine the source FPS. Double-click this row and choose Source FPS.";
        }

        if (!string.IsNullOrWhiteSpace(item.FpsWarning))
        {
            return item.FpsWarning;
        }

        if (!string.IsNullOrWhiteSpace(item.FpsDecisionReason))
        {
            return item.FpsDecisionReason;
        }

        return item.FpsSelectionMode == FpsSelectionMode.AutoDetect
            ? "Detected from Spotter frame records."
            : "Manual FPS selection.";
    }

    private static string FormatFpsDisplay(QueueItem item)
    {
        return item.RequiresManualFpsSelection || !item.HasResolvedFps
            ? "Needs manual selection"
            : item.FpsDisplayLabel;
    }

    private static string FormatFfmpegRateValue(QueueItem item)
    {
        return item.RequiresManualFpsSelection || !item.HasResolvedFps
            ? "Not set"
            : item.FfmpegRateValue;
    }

    private static string FormatFpsConfidence(QueueItem item)
    {
        return item.RequiresManualFpsSelection || !item.HasResolvedFps
            ? "Unavailable"
            : item.FpsConfidence;
    }

    private static string FormatExportSegment(QueueItem item)
    {
        return item.MultiFileExportContext?.DisplayText ?? "None detected";
    }

    private static string FormatSourceType(QueueItem item)
    {
        return item.IsSplitRecording ? "Split recording" : "Single DAT";
    }

    private static string FormatParts(QueueItem item)
    {
        return item.IsSplitRecording
            ? item.SplitExportPlan!.SegmentCount.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : "1";
    }

    private static string FormatSplitSegments(QueueItem item)
    {
        return item.IsSplitRecording
            ? string.Join(", ", item.SplitExportPlan!.Segments.Select(segment => segment.FileName))
            : "None";
    }

    private static string FormatOptionalExitCode(int? exitCode)
    {
        return exitCode?.ToString() ?? string.Empty;
    }

    private static string FormatField(string label, string? value)
    {
        return string.IsNullOrEmpty(value) ? $"{label}:" : $"{label}: {value}";
    }

    private static string FormatYesNo(bool value)
    {
        return value ? "Yes" : "No";
    }
}
