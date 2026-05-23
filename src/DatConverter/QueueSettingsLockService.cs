namespace DatConverter;

public static class QueueSettingsLockService
{
    public static void ApplyLockedSettings(
        QueueItem item,
        QueueSettingsSnapshot settings,
        string outputFolderPath,
        string plannedOutputPath,
        bool hasExistingDirectOutput,
        string readyProgressText)
    {
        item.PlannedOutputPath = plannedOutputPath;
        item.OutputDestinationMode = settings.OutputDestinationMode;
        item.SelectedOutputFolder = settings.OutputDestinationMode == OutputDestinationMode.ChooseOutputFolder
            ? outputFolderPath
            : null;
        item.OutputFormat = settings.OutputFormat;
        item.ConversionMode = settings.ConversionMode;
        item.Fps = settings.Fps;
        item.HasExistingDirectOutput = hasExistingDirectOutput;
        item.SkipIfDirectOutputExists = settings.SkipIfDirectOutputExists;

        if (hasExistingDirectOutput && settings.SkipIfDirectOutputExists)
        {
            item.Status = QueueItemStatus.Skipped;
            item.StatusText = "Exists";
            item.ProgressText = "Selected output exists";
            return;
        }

        item.Status = hasExistingDirectOutput ? QueueItemStatus.Warning : QueueItemStatus.Ready;
        item.StatusText = hasExistingDirectOutput ? "Already converted?" : "Ready";
        item.ProgressText = string.IsNullOrWhiteSpace(readyProgressText) ? "Ready" : readyProgressText;
    }
}
