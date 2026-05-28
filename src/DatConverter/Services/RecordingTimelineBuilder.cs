using System.Globalization;

namespace DatConverter;

public static class RecordingTimelineBuilder
{
    public static RecordingTimeline Build(QueueItem item)
    {
        if (item.SplitExportPlan is not null && item.IsSplitRecording)
        {
            return FromSplitExportPlan(item.SplitExportPlan, item.InputPath);
        }

        return FromSingleDat(
            item.InputPath,
            ParseDuration(item.PreProbeResult?.Duration),
            recordingStart: null,
            recordingEnd: null);
    }

    public static RecordingTimeline FromSingleDat(
        string sourcePath,
        TimeSpan? duration,
        DateTime? recordingStart,
        DateTime? recordingEnd)
    {
        recordingStart = NormalizeRecordingTimestamp(recordingStart);
        recordingEnd = NormalizeRecordingTimestamp(recordingEnd);
        duration ??= EstimateDatDuration(sourcePath);
        var resolvedDuration = ResolveDuration(duration, recordingStart, recordingEnd);
        var resolvedRecordingEnd = recordingEnd ?? (recordingStart.HasValue && resolvedDuration.HasValue ? recordingStart.Value + resolvedDuration.Value : null);
        return new RecordingTimeline
        {
            SourcePath = sourcePath,
            TotalDuration = resolvedDuration,
            RecordingStart = recordingStart,
            RecordingEnd = resolvedRecordingEnd,
            Segments =
            [
                new RecordingTimelineSegment
                {
                    SourcePath = sourcePath,
                    RecordingStart = recordingStart,
                    RecordingEnd = resolvedRecordingEnd,
                    Duration = resolvedDuration,
                    ElapsedOffset = TimeSpan.Zero
                }
            ]
        };
    }

    public static RecordingTimeline FromSplitExportPlan(SpotterSplitExportPlan plan, string sourcePath)
    {
        var orderedSegments = plan.Segments
            .OrderBy(segment => segment.SegmentNumber)
            .ToList();
        var timelineSegments = new List<RecordingTimelineSegment>(orderedSegments.Count);
        var elapsedOffset = TimeSpan.Zero;
        var hasUnknownDuration = false;

        foreach (var segment in orderedSegments)
        {
            var segmentStart = NormalizeRecordingTimestamp(segment.StartTime);
            var segmentEnd = NormalizeRecordingTimestamp(segment.EndTime);
            var duration = ResolveDuration(segment.Duration, segmentStart, segmentEnd) ?? EstimateDatDuration(segment.FilePath);
            timelineSegments.Add(new RecordingTimelineSegment
            {
                SourcePath = segment.FilePath,
                RecordingStart = segmentStart,
                RecordingEnd = segmentEnd,
                Duration = duration,
                ElapsedOffset = elapsedOffset
            });

            if (duration.HasValue)
            {
                elapsedOffset += duration.Value;
            }
            else
            {
                hasUnknownDuration = true;
            }
        }

        return new RecordingTimeline
        {
            SourcePath = sourcePath,
            Segments = timelineSegments,
            TotalDuration = hasUnknownDuration ? null : elapsedOffset,
            RecordingStart = timelineSegments.FirstOrDefault(segment => segment.RecordingStart.HasValue)?.RecordingStart,
            RecordingEnd = timelineSegments.LastOrDefault(segment => segment.RecordingEnd.HasValue)?.RecordingEnd
        };
    }

    private static DateTime? NormalizeRecordingTimestamp(DateTime? value)
    {
        if (!value.HasValue || value.Value <= DateTime.MinValue.AddDays(1) || value.Value.Year < 1970)
        {
            return null;
        }

        return value;
    }

    private static TimeSpan? ResolveDuration(TimeSpan? duration, DateTime? recordingStart, DateTime? recordingEnd)
    {
        if (duration.HasValue && duration.Value > TimeSpan.Zero)
        {
            return duration;
        }

        return recordingStart.HasValue && recordingEnd.HasValue && recordingEnd.Value > recordingStart.Value
            ? recordingEnd.Value - recordingStart.Value
            : null;
    }

    private static TimeSpan? ParseDuration(string? duration)
    {
        if (string.IsNullOrWhiteSpace(duration) || string.Equals(duration, "N/A", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (double.TryParse(duration, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds > 0)
        {
            return TimeSpan.FromSeconds(seconds);
        }

        return TimeSpan.TryParse(duration, CultureInfo.InvariantCulture, out var parsed) && parsed > TimeSpan.Zero
            ? parsed
            : null;
    }

    private static TimeSpan? EstimateDatDuration(string? sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) ||
            !string.Equals(Path.GetExtension(sourcePath), ".dat", StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(sourcePath))
        {
            return null;
        }

        try
        {
            var detection = new SpotterFpsDetector().Detect(sourcePath);
            var seconds = detection.TechnicalDetails.DurationSeconds;
            return detection.Succeeded && seconds.HasValue && seconds.Value > 0
                ? TimeSpan.FromSeconds(seconds.Value)
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }
}
