namespace DatConverter;

public sealed record QueueSettingsSnapshot(
    OutputFormat OutputFormat,
    string ConversionMode,
    FpsOption Fps,
    OutputDestinationMode OutputDestinationMode,
    string? ChosenOutputFolder)
{
    public QueueItemFpsSettings FpsSettings { get; init; } = QueueItemFpsSettings.FromManual(Fps);
}
