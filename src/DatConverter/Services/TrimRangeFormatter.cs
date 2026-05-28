using System.Globalization;

namespace DatConverter;

public static class TrimRangeFormatter
{
    public static string FormatTrimState(QueueItem item)
    {
        return item.TrimRange is null
            ? "Full video"
            : FormatRange(RecordingTimelineBuilder.Build(item), item.TrimRange);
    }

    public static string FormatRange(RecordingTimeline timeline, TrimRange range)
    {
        return $"{FormatOffset(timeline, range.Start)} \u2192 {FormatOffset(timeline, range.End)}";
    }

    public static string FormatOffset(RecordingTimeline timeline, TimeSpan offset)
    {
        if (timeline.RecordingStart.HasValue)
        {
            return (timeline.RecordingStart.Value + offset).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        return FormatElapsed(offset);
    }

    public static string FormatDuration(TimeSpan? start, TimeSpan? end)
    {
        if (!start.HasValue || !end.HasValue)
        {
            return "Full video";
        }

        if (end.Value <= start.Value)
        {
            return "\u2014";
        }

        return (end.Value - start.Value).ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
    }

    public static string FormatPreviewDuration(TimeSpan current, TimeSpan? start, TimeSpan? end)
    {
        var durationStart = start ?? TimeSpan.Zero;
        var durationEnd = end ?? current;
        if (durationEnd < durationStart)
        {
            return "\u2014";
        }

        return (durationEnd - durationStart).ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
    }

    private static string FormatElapsed(TimeSpan elapsed)
    {
        return elapsed.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
    }
}
