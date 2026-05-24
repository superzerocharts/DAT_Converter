using System.Globalization;

namespace DatConverter;

public sealed class QueueItemFpsSettings
{
    public FpsSelectionMode SelectionMode { get; init; }
    public string RequestedDisplayValue { get; init; } = "";
    public double? ManualFps { get; init; }
    public string ManualFfmpegRateValue { get; init; } = "30";

    public static QueueItemFpsSettings AutoDetect()
    {
        return new QueueItemFpsSettings
        {
            SelectionMode = FpsSelectionMode.AutoDetect,
            RequestedDisplayValue = "Auto-detect"
        };
    }

    public static QueueItemFpsSettings FromManual(FpsOption fps)
    {
        return new QueueItemFpsSettings
        {
            SelectionMode = FpsSelectionMode.Manual,
            RequestedDisplayValue = fps.Label,
            ManualFps = ParseManualFps(fps.Label),
            ManualFfmpegRateValue = fps.FfmpegValue
        };
    }

    public FpsOption ToManualFpsOption()
    {
        return new FpsOption(
            string.IsNullOrWhiteSpace(RequestedDisplayValue) ? "30" : RequestedDisplayValue,
            string.IsNullOrWhiteSpace(ManualFfmpegRateValue) ? "30" : ManualFfmpegRateValue);
    }

    private static double? ParseManualFps(string label)
    {
        return double.TryParse(label, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }
}
