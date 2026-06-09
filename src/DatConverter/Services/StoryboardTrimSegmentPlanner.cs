namespace DatConverter;

public static class StoryboardTrimSegmentPlanner
{
    public const string TimelineMode = "Storyboard order elapsed timeline (same model as trim preview)";

    public static StoryboardTrimSegmentPlan Build(SpotterStoryboardPlan plan, TrimRange? trimRange)
    {
        var orderedClips = plan.Clips
            .OrderBy(clip => clip.ClipNumber)
            .ToList();
        var timeline = RecordingTimelineBuilder.FromStoryboardPlan(
            plan,
            string.IsNullOrWhiteSpace(plan.SidecarPath) ? plan.ExportFolder : plan.SidecarPath);
        var selectedRange = ResolveSelectedRange(trimRange, timeline);
        var outputSegments = new List<StoryboardTrimSegment>();

        for (var index = 0; index < orderedClips.Count && index < timeline.Segments.Count; index++)
        {
            var clip = orderedClips[index];
            var timelineSegment = timeline.Segments[index];
            if (!timelineSegment.Duration.HasValue || timelineSegment.Duration.Value <= TimeSpan.Zero)
            {
                continue;
            }

            var clipStart = timelineSegment.ElapsedOffset;
            var clipEnd = clipStart + timelineSegment.Duration.Value;
            var intersectionStart = Max(selectedRange.Start, clipStart);
            var intersectionEnd = Min(selectedRange.End, clipEnd);
            if (intersectionEnd <= intersectionStart)
            {
                continue;
            }

            outputSegments.Add(new StoryboardTrimSegment
            {
                OutputSegmentNumber = outputSegments.Count + 1,
                ClipIndex = index,
                ClipNumber = clip.ClipNumber,
                ClipName = clip.ClipName,
                CameraDisplayName = clip.CameraDisplayName,
                SourceDatPath = clip.DatFilePath,
                SourceFpsLabel = clip.SourceFpsLabel,
                SourceFfmpegFpsValue = clip.SourceFfmpegFpsValue,
                SourceFpsDecisionReason = clip.SourceFpsDecisionReason,
                ClipStoryboardStart = clipStart,
                ClipStoryboardEnd = clipEnd,
                SelectedIntersectionStart = intersectionStart,
                SelectedIntersectionEnd = intersectionEnd,
                LocalStartOffset = intersectionStart - clipStart,
                LocalDuration = intersectionEnd - intersectionStart
            });
        }

        return new StoryboardTrimSegmentPlan
        {
            SelectedRange = selectedRange,
            TimelineMode = TimelineMode,
            TotalStoryboardClips = orderedClips.Count,
            Segments = outputSegments
        };
    }

    private static TrimRange ResolveSelectedRange(TrimRange? trimRange, RecordingTimeline timeline)
    {
        if (trimRange is not null)
        {
            return trimRange;
        }

        var end = timeline.TotalDuration ?? timeline.Segments
            .Where(segment => segment.Duration.HasValue)
            .Select(segment => segment.ElapsedOffset + segment.Duration!.Value)
            .DefaultIfEmpty(TimeSpan.Zero)
            .Max();
        return new TrimRange(TimeSpan.Zero, end);
    }

    private static TimeSpan Max(TimeSpan left, TimeSpan right)
    {
        return left >= right ? left : right;
    }

    private static TimeSpan Min(TimeSpan left, TimeSpan right)
    {
        return left <= right ? left : right;
    }
}
