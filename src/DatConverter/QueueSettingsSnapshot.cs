namespace DatConverter;

public sealed record QueueSettingsSnapshot(
    OutputFormat OutputFormat,
    string ConversionMode,
    FpsOption Fps,
    OutputDestinationMode OutputDestinationMode,
    string? ChosenOutputFolder,
    bool BurnTimestamp = false)
{
    public QueueItemFpsSettings FpsSettings { get; init; } = QueueItemFpsSettings.FromManual(Fps);
}
