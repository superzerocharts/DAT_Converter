using System.Globalization;

namespace DatConverter;

public static class TrimOutputNameFormatter
{
    public static string BuildTrimSuffix(RecordingTimeline timeline, TrimRange trimRange)
    {
        return timeline.RecordingStart.HasValue
            ? "_trim_" + FormatDateTime(timeline.RecordingStart.Value + trimRange.Start) + "-" + FormatDateTime(timeline.RecordingStart.Value + trimRange.End)
            : "_trim_" + FormatElapsed(trimRange.Start) + "-" + FormatElapsed(trimRange.End);
    }

    private static string FormatDateTime(DateTime value)
    {
        return value.ToString("yyMMdd_HHmm", CultureInfo.InvariantCulture);
    }

    private static string FormatElapsed(TimeSpan value)
    {
        var totalHours = Math.Max(0, (int)Math.Floor(value.TotalHours));
        return totalHours.ToString("00", CultureInfo.InvariantCulture) +
               value.Minutes.ToString("00", CultureInfo.InvariantCulture) +
               value.Seconds.ToString("00", CultureInfo.InvariantCulture);
    }
}
