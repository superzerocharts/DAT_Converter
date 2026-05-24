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
        FpsSettings = QueueItemFpsSettings.FromManual(fps);
        Fps = fps;
        ApplyFpsResolution(FpsSettings, QueueItemFpsResolution.FromManual(fps));
        HasExistingDirectOutput = hasExistingDirectOutput;
        Status = QueueItemStatus.WaitingForProbe;
        StatusText = QueueItemStatusText.CheckingFile;
    }

    public string InputPath { get; }

    public string PlannedOutputPath { get; set; }

    public OutputDestinationMode OutputDestinationMode { get; set; }

    public string? SelectedOutputFolder { get; set; }

    public OutputFormat OutputFormat { get; set; }

    public string ConversionMode { get; set; }

    public FpsOption Fps { get; set; }

    public QueueItemFpsSettings FpsSettings { get; private set; }

    public FpsSelectionMode FpsSelectionMode { get; private set; }

    public string FpsDisplayLabel { get; private set; } = "30 fps";

    public string FfmpegRateValue { get; private set; } = "30";

    public double? NominalConversionFps { get; private set; } = 30;

    public bool HasResolvedFps { get; private set; } = true;

    public bool RequiresManualFpsSelection { get; private set; }

    public string? FpsValidationMessage { get; private set; }

    public bool FpsAutoDetectionSucceeded { get; private set; }

    public string FpsConfidence { get; private set; } = "Manual";

    public string? FpsWarning { get; private set; }

    public string? FpsDecisionReason { get; private set; }

    public string? FpsTechnicalLogText { get; private set; }

    public bool HasExistingDirectOutput { get; set; }

    public string? CustomOutputPath { get; set; }

    public bool HasCustomOutputPath { get; set; }

    public bool HasCustomFormat { get; set; }

    public bool HasCustomMode { get; set; }

    public bool HasCustomFpsSetting { get; set; }

    public ProbeResult? PreProbeResult { get; set; }

    public ConversionResult? ConversionResult { get; set; }

    public string? ResultStatusSummary { get; set; }

    public QueueItemStatus Status { get; set; }

    public string StatusText { get; set; }

    public string ProgressText { get; set; } = "";

    public void ApplyFpsResolution(QueueItemFpsResolution resolution)
    {
        ApplyFpsResolution(FpsSettings, resolution);
    }

    public void ApplyFpsResolution(QueueItemFpsSettings settings, QueueItemFpsResolution resolution)
    {
        FpsSettings = settings;
        FpsSelectionMode = resolution.SelectionMode;
        FpsDisplayLabel = resolution.DisplayLabel;
        FfmpegRateValue = resolution.FfmpegRateValue;
        NominalConversionFps = resolution.NominalConversionFps;
        HasResolvedFps = resolution.HasResolvedFps;
        RequiresManualFpsSelection = resolution.RequiresManualFpsSelection;
        FpsValidationMessage = resolution.FpsValidationMessage;
        FpsAutoDetectionSucceeded = resolution.AutoDetectionSucceeded;
        FpsConfidence = resolution.Confidence;
        FpsWarning = resolution.Warning;
        FpsDecisionReason = resolution.DecisionReason;
        FpsTechnicalLogText = resolution.TechnicalLogText;
        Fps = resolution.ToFpsOption();
    }

    public void ClearCustomSettings()
    {
        CustomOutputPath = null;
        HasCustomOutputPath = false;
        HasCustomFormat = false;
        HasCustomMode = false;
        HasCustomFpsSetting = false;
    }
}
