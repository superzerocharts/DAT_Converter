using System.Globalization;

namespace DatConverter;

public sealed class ConversionProgressParser
{
    private readonly TimeSpan? duration;
    private readonly Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);

    public ConversionProgressParser(TimeSpan? duration)
    {
        this.duration = duration;
    }

    public ConversionProgress? LastProgress { get; private set; }

    public ConversionProgress? ParseLine(string line)
    {
        var separatorIndex = line.IndexOf('=');
        if (separatorIndex <= 0)
        {
            return null;
        }

        var key = line[..separatorIndex].Trim();
        var value = line[(separatorIndex + 1)..].Trim();
        values[key] = value;

        if (!string.Equals(key, "progress", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var outputTime = GetOutputTime();
        var percent = GetPercent(outputTime);
        var isEnd = string.Equals(value, "end", StringComparison.OrdinalIgnoreCase);
        LastProgress = new ConversionProgress(
            percent,
            outputTime,
            GetValue("frame"),
            GetValue("speed"),
            isEnd,
            BuildSummary(percent, outputTime, GetValue("frame"), GetValue("fps"), GetValue("speed")),
            GetValue("fps"),
            GetValue("bitrate"),
            GetValue("total_size"),
            GetValue("dup_frames"),
            GetValue("drop_frames"),
            GetValue("out_time_us"),
            GetValue("out_time_ms"));
        return LastProgress;
    }

    private TimeSpan? GetOutputTime()
    {
        var outTimeUs = GetLong("out_time_us");
        if (outTimeUs.HasValue && outTimeUs.Value >= 0)
        {
            return TimeSpan.FromMilliseconds(outTimeUs.Value / 1000D);
        }

        var outTimeMs = GetLong("out_time_ms");
        if (outTimeMs.HasValue && outTimeMs.Value >= 0)
        {
            return TimeSpan.FromMilliseconds(outTimeMs.Value / 1000D);
        }

        var outTime = GetValue("out_time");
        if (!string.IsNullOrWhiteSpace(outTime) && TimeSpan.TryParse(outTime, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private int? GetPercent(TimeSpan? outputTime)
    {
        if (!duration.HasValue || duration.Value <= TimeSpan.Zero || !outputTime.HasValue)
        {
            return null;
        }

        var percent = (int)Math.Round(outputTime.Value.TotalMilliseconds / duration.Value.TotalMilliseconds * 100D);
        return Math.Clamp(percent, 0, 100);
    }

    private string? GetValue(string key)
    {
        return values.TryGetValue(key, out var value) ? value : null;
    }

    private long? GetLong(string key)
    {
        return long.TryParse(GetValue(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    private static string BuildSummary(int? percent, TimeSpan? outputTime, string? frame, string? fps, string? speed)
    {
        var parts = new List<string>();

        if (percent.HasValue)
        {
            parts.Add($"{percent.Value}%");
        }

        if (outputTime.HasValue)
        {
            parts.Add($"output time {FormatTime(outputTime.Value)}");
        }

        if (!string.IsNullOrWhiteSpace(frame))
        {
            parts.Add($"frame {frame}");
        }

        if (!string.IsNullOrWhiteSpace(fps))
        {
            parts.Add($"fps {fps}");
        }

        if (!string.IsNullOrWhiteSpace(speed))
        {
            parts.Add($"speed {speed}");
        }

        return parts.Count == 0 ? "processing" : string.Join(", ", parts);
    }

    private static string FormatTime(TimeSpan time)
    {
        return time.TotalHours >= 1
            ? time.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : time.ToString(@"mm\:ss", CultureInfo.InvariantCulture);
    }
}
