using System.Globalization;
using System.Text;

namespace DatConverter;

public sealed class StoryboardTrimSegmentPlan
{
    public required TrimRange SelectedRange { get; init; }

    public required string TimelineMode { get; init; }

    public required int TotalStoryboardClips { get; init; }

    public required IReadOnlyList<StoryboardTrimSegment> Segments { get; init; }

    public int IntersectingSegmentCount => Segments.Count;

    public int SkippedClipCount => Math.Max(0, TotalStoryboardClips - IntersectingSegmentCount);

    public TimeSpan PlannedOutputDuration => TimeSpan.FromTicks(Segments.Sum(segment => segment.LocalDuration.Ticks));

    public bool DropsGaps => true;

    public string BuildTechnicalReport()
    {
        var builder = new StringBuilder();
        builder.AppendLine("Storyboard trim segment plan:");
        builder.AppendLine(CultureInfo.InvariantCulture, $"- selected trim start/end: {FormatDuration(SelectedRange.Start)} to {FormatDuration(SelectedRange.End)}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"- timeline mode used by planner: {TimelineMode}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"- total storyboard clips: {TotalStoryboardClips}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"- intersecting segment count: {IntersectingSegmentCount}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"- skipped clip count: {SkippedClipCount}");
        foreach (var segment in Segments)
        {
            builder.AppendLine(
                CultureInfo.InvariantCulture,
                $"- Segment {segment.OutputSegmentNumber}: clip {segment.ClipNumber}, camera={FormatValue(segment.CameraDisplayName)}, local start={FormatDuration(segment.LocalStartOffset)}, duration={FormatDuration(segment.LocalDuration)}, source={segment.SourceDatPath}");
        }

        builder.AppendLine(CultureInfo.InvariantCulture, $"- total planned output duration: {FormatDuration(PlannedOutputDuration)}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"- gaps are {(DropsGaps ? "dropped" : "preserved")}");
        return builder.ToString();
    }

    private static string FormatDuration(TimeSpan value)
    {
        return value.ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture);
    }

    private static string FormatValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "unknown" : value;
    }
}

public sealed class StoryboardTrimSegment
{
    public required int OutputSegmentNumber { get; init; }

    public required int ClipIndex { get; init; }

    public required int ClipNumber { get; init; }

    public string? ClipName { get; init; }

    public string? CameraDisplayName { get; init; }

    public required string SourceDatPath { get; init; }

    public string? SourceFpsLabel { get; init; }

    public string? SourceFfmpegFpsValue { get; init; }

    public string? SourceFpsDecisionReason { get; init; }

    public required TimeSpan ClipStoryboardStart { get; init; }

    public required TimeSpan ClipStoryboardEnd { get; init; }

    public required TimeSpan SelectedIntersectionStart { get; init; }

    public required TimeSpan SelectedIntersectionEnd { get; init; }

    public required TimeSpan LocalStartOffset { get; init; }

    public required TimeSpan LocalDuration { get; init; }
}
