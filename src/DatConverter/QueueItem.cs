namespace DatConverter;

public sealed class QueueItem
{
    public QueueItem(
        string inputPath,
        string plannedOutputPath,
        OutputDestinationMode outputDestinationMode,
        string? selectedOutputFolder,
        OutputFormat outputFormat,
        string conversionMode,
        FpsOption fps,
        bool hasExistingDirectOutput)
    {
        InputPath = inputPath;
        PlannedOutputPath = plannedOutputPath;
        OutputDestinationMode = outputDestinationMode;
        SelectedOutputFolder = selectedOutputFolder;
        OutputFormat = outputFormat;
        ConversionMode = conversionMode;
        Fps = fps;
        HasExistingDirectOutput = hasExistingDirectOutput;
        Status = QueueItemStatus.WaitingForProbe;
        StatusText = "Waiting for probe";
    }

    public string InputPath { get; }

    public string PlannedOutputPath { get; set; }

    public OutputDestinationMode OutputDestinationMode { get; set; }

    public string? SelectedOutputFolder { get; set; }

    public OutputFormat OutputFormat { get; set; }

    public string ConversionMode { get; set; }

    public FpsOption Fps { get; set; }

    public bool HasExistingDirectOutput { get; set; }

    public string? CustomOutputPath { get; set; }

    public ProbeResult? PreProbeResult { get; set; }

    public QueueItemStatus Status { get; set; }

    public string StatusText { get; set; }

    public string ProgressText { get; set; } = "";
}
