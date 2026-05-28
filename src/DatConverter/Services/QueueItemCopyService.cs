namespace DatConverter;

public static class QueueItemCopyService
{
    public static QueueItem CreateReadyCopy(
        QueueItem source,
        string plannedOutputPath,
        OutputDestinationMode outputDestinationMode,
        string? selectedOutputFolder,
        OutputFormat outputFormat,
        QueueItemFpsResolution fpsResolution,
        QueueItemFpsSettings fpsSettings)
    {
        var copy = new QueueItem(
            source.InputPath,
            plannedOutputPath,
            outputDestinationMode,
            selectedOutputFolder,
            outputFormat,
            "Fast",
            source.Fps,
            hasExistingDirectOutput: false);

        copy.ApplyFpsResolution(fpsSettings, fpsResolution);
        copy.MultiFileExportContext = source.MultiFileExportContext;
        copy.SplitExportPlan = source.SplitExportPlan;
        copy.LogicalOutputBaseName = source.LogicalOutputBaseName;
        copy.TrimRange = null;
        copy.BurnTimestamp = false;
        copy.HasCustomFormat = false;
        copy.HasCustomMode = false;
        copy.HasCustomFpsSetting = false;
        copy.CustomOutputPath = source.IsSplitRecording ? plannedOutputPath : null;
        copy.HasCustomOutputPath = source.IsSplitRecording;
        copy.HasUserCustomOutputPath = false;
        copy.PreProbeResult = null;
        copy.ConversionResult = null;
        copy.ResultStatusSummary = null;
        copy.HasExistingDirectOutput = false;

        if (copy.RequiresManualFpsSelection || !copy.HasResolvedFps)
        {
            copy.PreProbeResult = null;
            copy.Status = QueueItemStatus.Warning;
            copy.StatusText = "Needs FPS";
            copy.ProgressText = "Choose Source FPS";
            return copy;
        }

        copy.Status = QueueItemStatus.Ready;
        copy.StatusText = "Ready";
        copy.ProgressText = "Ready";
        return copy;
    }
}
