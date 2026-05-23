namespace DatConverter;

public sealed record QueueSettingsSnapshot(
    OutputFormat OutputFormat,
    string ConversionMode,
    FpsOption Fps,
    OutputDestinationMode OutputDestinationMode,
    string? ChosenOutputFolder);
