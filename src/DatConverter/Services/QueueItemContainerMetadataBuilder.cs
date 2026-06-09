using System.Globalization;

namespace DatConverter;

public static class QueueItemContainerMetadataBuilder
{
    public static ContainerMetadata Build(QueueItem item)
    {
        var timeline = RecordingTimelineBuilder.Build(item);
        var creationTime = ResolveCreationTime(timeline, item.TrimRange);
        var title = ResolveTitle(item);
        var comment = BuildComment(item, timeline);
        return new ContainerMetadata(creationTime, title, comment);
    }

    private static DateTime? ResolveCreationTime(RecordingTimeline timeline, TrimRange? trimRange)
    {
        if (!timeline.RecordingStart.HasValue)
        {
            return null;
        }

        return trimRange is null
            ? timeline.RecordingStart.Value
            : timeline.RecordingStart.Value + trimRange.Start;
    }

    private static string? ResolveTitle(QueueItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.LogicalOutputBaseName))
        {
            return item.LogicalOutputBaseName;
        }

        if (!string.IsNullOrWhiteSpace(item.SplitExportPlan?.LogicalOutputBaseName))
        {
            return item.SplitExportPlan.LogicalOutputBaseName;
        }

        var sourceBaseName = Path.GetFileNameWithoutExtension(item.InputPath);
        return string.IsNullOrWhiteSpace(sourceBaseName) ? null : sourceBaseName;
    }

    private static string? BuildComment(QueueItem item, RecordingTimeline timeline)
    {
        var parts = new List<string>
        {
            item.IsCombinedStoryboard ? "Source type: Storyboard" : item.IsStoryboardClip ? "Source type: Storyboard clip" : item.IsSplitRecording ? "Source type: Split recording" : "Source type: Single DAT"
        };

        if (item.IsCombinedStoryboard && item.StoryboardPlan is not null)
        {
            parts.Add($"Storyboard clips: {item.StoryboardPlan.ClipCount}");
            parts.Add("Storyboard clips will be combined in storyboard order");
        }

        if (item.StoryboardClip is not null)
        {
            parts.Add($"Storyboard clip: {item.StoryboardClip.ClipNumber} of {item.StoryboardPlan?.ClipCount ?? 0}");
            if (!string.IsNullOrWhiteSpace(item.StoryboardClip.CameraDisplayName))
            {
                parts.Add($"Camera: {item.StoryboardClip.CameraDisplayName}");
            }
        }

        if (timeline.RecordingStart.HasValue)
        {
            parts.Add($"Recording: {FormatDateTime(timeline.RecordingStart.Value)} to {FormatDateTime(timeline.RecordingEnd)}");
        }

        if (item.TrimRange is not null)
        {
            var trimDuration = item.TrimRange.End - item.TrimRange.Start;
            var start = FormatOffset(timeline, item.TrimRange.Start);
            var end = FormatOffset(timeline, item.TrimRange.End);
            parts.Add($"Trim: {start} to {end}");
            parts.Add($"Trim duration: {FormatDuration(trimDuration)}");
        }

        return string.Join("; ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private static string FormatOffset(RecordingTimeline timeline, TimeSpan offset)
    {
        return timeline.RecordingStart.HasValue
            ? FormatDateTime(timeline.RecordingStart.Value + offset)
            : FormatDuration(offset);
    }

    private static string FormatDateTime(DateTime? value)
    {
        return value.HasValue
            ? value.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
            : "unknown";
    }

    private static string FormatDuration(TimeSpan value)
    {
        var totalHours = Math.Max(0, (int)Math.Floor(value.TotalHours));
        return totalHours.ToString("00", CultureInfo.InvariantCulture) + ":" +
               value.Minutes.ToString("00", CultureInfo.InvariantCulture) + ":" +
               value.Seconds.ToString("00", CultureInfo.InvariantCulture);
    }
}
