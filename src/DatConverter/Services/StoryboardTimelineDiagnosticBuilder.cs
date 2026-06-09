using System.Globalization;
using System.Text;

namespace DatConverter;

public static class StoryboardTimelineDiagnosticBuilder
{
    public static string Build(
        SpotterStoryboardPlan plan,
        TrimRange? trimRange,
        StoryboardTrimSegmentPlan segmentPlan,
        IReadOnlyList<string> normalizedSegmentPaths,
        Func<string, SpotterFpsDetectionResult>? detectClip = null)
    {
        detectClip ??= path => new SpotterFpsDetector().Detect(path);
        var orderedClips = plan.Clips.OrderBy(clip => clip.ClipNumber).ToList();
        var timeline = RecordingTimelineBuilder.FromStoryboardPlan(
            plan,
            string.IsNullOrWhiteSpace(plan.SidecarPath) ? plan.ExportFolder : plan.SidecarPath);
        var segmentByClipIndex = segmentPlan.Segments.ToDictionary(segment => segment.ClipIndex);
        var builder = new StringBuilder();

        builder.AppendLine("Storyboard Timeline Diagnostic");
        builder.AppendLine("Selected trim range:");
        builder.AppendLine(CultureInfo.InvariantCulture, $"- raw start value: {FormatDuration(segmentPlan.SelectedRange.Start)}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"- raw end value: {FormatDuration(segmentPlan.SelectedRange.End)}");
        builder.AppendLine("- value basis: storyboard timeline offset");
        builder.AppendLine(CultureInfo.InvariantCulture, $"- selected duration: {FormatDuration(segmentPlan.SelectedRange.End - segmentPlan.SelectedRange.Start)}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"- trim dialog timeline basis: {StoryboardTrimSegmentPlanner.TimelineMode}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"- merge planner timeline basis: {segmentPlan.TimelineMode}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"- trim dialog and merge planner basis match: {FormatYesNo(string.Equals(StoryboardTrimSegmentPlanner.TimelineMode, segmentPlan.TimelineMode, StringComparison.Ordinal))}");

        builder.AppendLine("Storyboard clip table:");
        for (var index = 0; index < orderedClips.Count && index < timeline.Segments.Count; index++)
        {
            var clip = orderedClips[index];
            var timelineSegment = timeline.Segments[index];
            var detection = SafeDetect(detectClip, clip.DatFilePath);
            var clipDuration = timelineSegment.Duration ?? TimeSpan.Zero;
            builder.AppendLine(
                CultureInfo.InvariantCulture,
                $"- Clip {index + 1}: clip_number={clip.ClipNumber}; camera={FormatValue(clip.CameraDisplayName ?? clip.ClipName)}; dat={clip.DatFilePath}; source_start={FormatDateTime(clip.EffectiveStartTime)}; source_end={FormatDateTime(clip.EffectiveEndTime)}; source_duration={FormatDuration(clip.EffectiveDuration)}; storyboard_start={FormatDuration(timelineSegment.ElapsedOffset)}; storyboard_end={FormatDuration(timelineSegment.ElapsedOffset + clipDuration)}; storyboard_duration={FormatDuration(timelineSegment.Duration)}; local_start=00:00:00.000; local_end={FormatDuration(clipDuration)}; fps={FormatFps(detection)}; resolution={FormatResolution(detection)}");
        }

        builder.AppendLine("Intersecting segment plan:");
        for (var index = 0; index < segmentPlan.Segments.Count; index++)
        {
            var segment = segmentPlan.Segments[index];
            var normalizedPath = index < normalizedSegmentPaths.Count ? normalizedSegmentPaths[index] : "(not planned)";
            builder.AppendLine(
                CultureInfo.InvariantCulture,
                $"- Segment {segment.OutputSegmentNumber}: source_clip_index={segment.ClipIndex + 1}; clip_number={segment.ClipNumber}; camera={FormatValue(segment.CameraDisplayName ?? segment.ClipName)}; storyboard_intersection={FormatDuration(segment.SelectedIntersectionStart)} to {FormatDuration(segment.SelectedIntersectionEnd)}; source_intersection={FormatSourceIntersection(orderedClips, segment)}; local_start={FormatDuration(segment.LocalStartOffset)}; local_duration={FormatDuration(segment.LocalDuration)}; normalized_output={normalizedPath}");
        }

        builder.AppendLine("Skipped clips:");
        for (var index = 0; index < orderedClips.Count; index++)
        {
            if (segmentByClipIndex.ContainsKey(index))
            {
                continue;
            }

            var clip = orderedClips[index];
            builder.AppendLine(
                CultureInfo.InvariantCulture,
                $"- Clip {index + 1}: clip_number={clip.ClipNumber}; camera={FormatValue(clip.CameraDisplayName ?? clip.ClipName)}; reason=does not intersect selected storyboard timeline trim range");
        }

        builder.AppendLine("Final concat plan:");
        for (var index = 0; index < segmentPlan.Segments.Count; index++)
        {
            var segment = segmentPlan.Segments[index];
            var normalizedPath = index < normalizedSegmentPaths.Count ? normalizedSegmentPaths[index] : "(not planned)";
            builder.AppendLine(
                CultureInfo.InvariantCulture,
                $"- Concat {index + 1}: normalized_file={normalizedPath}; expected_segment_duration={FormatDuration(segment.LocalDuration)}");
        }

        builder.AppendLine(CultureInfo.InvariantCulture, $"- total expected output duration: {FormatDuration(segmentPlan.PlannedOutputDuration)}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"- gaps are {(segmentPlan.DropsGaps ? "dropped" : "preserved")}");

        builder.AppendLine("Safety warnings:");
        var warnings = BuildSafetyWarnings(segmentPlan).ToList();
        if (warnings.Count == 0)
        {
            builder.AppendLine("- none");
        }
        else
        {
            foreach (var warning in warnings)
            {
                builder.AppendLine(CultureInfo.InvariantCulture, $"- {warning}");
            }
        }

        return builder.ToString();
    }

    public static bool PlannerUsesTrimPreviewTimelineBasis(StoryboardTrimSegmentPlan segmentPlan)
    {
        return string.Equals(StoryboardTrimSegmentPlanner.TimelineMode, segmentPlan.TimelineMode, StringComparison.Ordinal);
    }

    private static IEnumerable<string> BuildSafetyWarnings(StoryboardTrimSegmentPlan segmentPlan)
    {
        if (!PlannerUsesTrimPreviewTimelineBasis(segmentPlan))
        {
            yield return "Timeline mismatch suspected: trim preview and merge planner do not agree.";
        }

        if (segmentPlan.Segments.Count == 1)
        {
            yield return "Selected trim currently intersects only one storyboard segment according to the planner.";
        }
    }

    private static SpotterFpsDetectionResult? SafeDetect(Func<string, SpotterFpsDetectionResult> detectClip, string path)
    {
        try
        {
            return detectClip(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or NotSupportedException)
        {
            return new SpotterFpsDetectionResult
            {
                Succeeded = false,
                FailureReason = ex.Message
            };
        }
    }

    private static string FormatSourceIntersection(IReadOnlyList<SpotterStoryboardClip> orderedClips, StoryboardTrimSegment segment)
    {
        if (segment.ClipIndex < 0 || segment.ClipIndex >= orderedClips.Count)
        {
            return "unknown";
        }

        var clip = orderedClips[segment.ClipIndex];
        if (!clip.EffectiveStartTime.HasValue)
        {
            return "unknown";
        }

        var start = clip.EffectiveStartTime.Value + segment.LocalStartOffset;
        var end = start + segment.LocalDuration;
        return $"{FormatDateTime(start)} to {FormatDateTime(end)}";
    }

    private static string FormatFps(SpotterFpsDetectionResult? detection)
    {
        if (detection?.Succeeded != true)
        {
            return "unknown";
        }

        var fps = detection.TechnicalDetails.BucketMedianFps ?? detection.TechnicalDetails.AverageFps;
        return fps.HasValue ? fps.Value.ToString("0.###", CultureInfo.InvariantCulture) : "unknown";
    }

    private static string FormatResolution(SpotterFpsDetectionResult? detection)
    {
        var details = detection?.TechnicalDetails;
        return details?.Width.HasValue == true && details.Height.HasValue
            ? $"{details.Width.Value}x{details.Height.Value}"
            : "unknown";
    }

    private static string FormatDateTime(DateTime? value)
    {
        return value.HasValue ? FormatDateTime(value.Value) : "unknown";
    }

    private static string FormatDateTime(DateTime value)
    {
        return value.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
    }

    private static string FormatDuration(TimeSpan? value)
    {
        return value.HasValue ? FormatDuration(value.Value) : "unknown";
    }

    private static string FormatDuration(TimeSpan value)
    {
        return value.ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture);
    }

    private static string FormatValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "unknown" : value;
    }

    private static string FormatYesNo(bool value)
    {
        return value ? "Yes" : "No";
    }
}
