namespace DatConverter;

public sealed class FileSelectionState
{
    public string? SelectedInputFilePath { get; set; }

    public string? SelectedOutputFolderPath { get; set; }

    public string? ChosenOutputFolderPath { get; set; }

    public string? PlannedOutputFilePath { get; set; }

    public OutputDestinationMode OutputDestinationMode { get; set; } = OutputDestinationMode.SameFolderAsSource;

    public bool IsInputFileValid { get; set; }

    public bool IsOutputFolderValid { get; set; }

    public bool IsPlannedOutputPathValid => !string.IsNullOrWhiteSpace(PlannedOutputFilePath);

    public bool IsProbeValid { get; set; }

    public bool IsProbeRunning { get; set; }

    public ProbeResult? LastProbeResult { get; set; }
}
