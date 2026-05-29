namespace DatConverter;

public static class BurnTimestampMetadataBuilder
{
    public const string RequiresFullModeMessage = "Burn timestamp requires Full mode.";
    public const string RequiresRecordingDateTimeMessage = "Burn timestamp requires recording date/time.";
    public const string BundledFfmpegUnavailableMessage = "Burn timestamp is not available with the bundled FFmpeg build.";
    public const string ReliabilityNote = "Uses recording time if available.";

    public static BurnTimestampOptions? Build(QueueItem item, RecordingTimeline timeline)
    {
        if (!item.BurnTimestamp || !timeline.RecordingStart.HasValue)
        {
            return null;
        }

        var startTime = item.TrimRange is null
            ? timeline.RecordingStart.Value
            : timeline.RecordingStart.Value + item.TrimRange.Start;

        var font = BurnTimestampFontResolver.Resolve();
        return new BurnTimestampOptions(ResolveCameraName(item), startTime, font.FontFilePath, font.Warning);
    }

    public static bool IsSupportedMode(string? conversionMode)
    {
        return ConversionModes.IsEncode(ConversionModes.ParseDisplay(conversionMode));
    }

    private static string ResolveCameraName(QueueItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.SplitExportPlan?.CameraDisplayName))
        {
            return item.SplitExportPlan.CameraDisplayName;
        }

        if (!string.IsNullOrWhiteSpace(item.LogicalOutputBaseName))
        {
            return item.LogicalOutputBaseName;
        }

        if (!string.IsNullOrWhiteSpace(item.SplitExportPlan?.LogicalOutputBaseName))
        {
            return item.SplitExportPlan.LogicalOutputBaseName;
        }

        var sourceBaseName = Path.GetFileNameWithoutExtension(item.InputPath);
        return string.IsNullOrWhiteSpace(sourceBaseName) ? "Camera" : sourceBaseName;
    }
}
