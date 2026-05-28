namespace DatConverter;

public static class QueueGridRowFormatter
{
    public static string FormatResolution(QueueItem item)
    {
        if (item.PreProbeResult?.IsSuccess == true)
        {
            return FormatResolution(item.PreProbeResult.Width, item.PreProbeResult.Height);
        }

        return string.Empty;
    }

    public static string FormatProgress(QueueItem item)
    {
        if (IsResolutionText(item.ProgressText))
        {
            return string.Empty;
        }

        return item.ProgressText ?? string.Empty;
    }

    private static string FormatResolution(int? width, int? height)
    {
        return width.HasValue && height.HasValue
            ? $"{width.Value}x{height.Value}"
            : "Unknown";
    }

    private static bool IsResolutionText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Split('x', 'X');
        return parts.Length == 2 &&
               int.TryParse(parts[0], out var width) &&
               int.TryParse(parts[1], out var height) &&
               width > 0 &&
               height > 0;
    }
}
