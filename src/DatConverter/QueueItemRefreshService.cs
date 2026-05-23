namespace DatConverter;

public static class QueueItemRefreshService
{
    public static QueueRefreshResult RefreshEditableItems(
        IEnumerable<QueueItem> items,
        QueueSettingsSnapshot settings,
        Func<QueueItem, QueueSettingsSnapshot, string?> resolveOutputFolder,
        Func<QueueItem, string, OutputFormat, string?> planOutputPath,
        Func<QueueItem, string, OutputFormat, string?> getDirectOutputPath)
    {
        var refreshed = 0;
        var invalid = 0;

        foreach (var item in items.Where(CanRefreshFromLiveSettings))
        {
            var outputFolderPath = resolveOutputFolder(item, settings);
            var outputFolderValidation = OutputFolderValidator.ValidateOutputFolder(outputFolderPath);
            if (!outputFolderValidation.IsValid || string.IsNullOrWhiteSpace(outputFolderValidation.FolderPath))
            {
                ApplySettings(item, settings, null, null, hasExistingDirectOutput: false);
                item.Status = QueueItemStatus.Invalid;
                item.StatusText = "Output invalid";
                item.ProgressText = outputFolderValidation.Message;
                item.PreProbeResult = null;
                invalid++;
                continue;
            }

            var directOutputPath = getDirectOutputPath(item, outputFolderValidation.FolderPath, settings.OutputFormat);
            var hasExistingDirectOutput = !string.IsNullOrWhiteSpace(directOutputPath) && File.Exists(directOutputPath);
            var plannedOutputPath = planOutputPath(item, outputFolderValidation.FolderPath, settings.OutputFormat);
            if (string.IsNullOrWhiteSpace(plannedOutputPath))
            {
                ApplySettings(item, settings, outputFolderValidation.FolderPath, null, hasExistingDirectOutput);
                item.Status = QueueItemStatus.Invalid;
                item.StatusText = "No output";
                item.ProgressText = "No safe output path";
                item.PreProbeResult = null;
                invalid++;
                continue;
            }

            var wasProbeValidForSettings =
                item.PreProbeResult?.IsSuccess == true &&
                string.Equals(item.Fps.Label, settings.Fps.Label, StringComparison.Ordinal) &&
                string.Equals(item.Fps.FfmpegValue, settings.Fps.FfmpegValue, StringComparison.Ordinal);
            var readyProgressText = wasProbeValidForSettings && item.PreProbeResult is not null
                ? FormatProbeProgressText(item.PreProbeResult)
                : "Waiting for probe";

            ApplySettings(item, settings, outputFolderValidation.FolderPath, plannedOutputPath, hasExistingDirectOutput);

            if (!wasProbeValidForSettings)
            {
                item.PreProbeResult = null;
                item.Status = QueueItemStatus.WaitingForProbe;
                item.StatusText = "Waiting for probe";
                item.ProgressText = "";
            }
            else if (hasExistingDirectOutput)
            {
                item.Status = QueueItemStatus.Skipped;
                item.StatusText = "Exists";
                item.ProgressText = "Selected output exists";
            }
            else
            {
                item.Status = QueueItemStatus.Ready;
                item.StatusText = "Ready";
                item.ProgressText = readyProgressText;
            }

            refreshed++;
        }

        return new QueueRefreshResult(refreshed, invalid);
    }

    public static bool CanRefreshFromLiveSettings(QueueItem item)
    {
        return item.Status is QueueItemStatus.WaitingForProbe
            or QueueItemStatus.Ready
            or QueueItemStatus.Warning
            or QueueItemStatus.Skipped
            or QueueItemStatus.Unsupported
            or QueueItemStatus.Invalid;
    }

    private static void ApplySettings(
        QueueItem item,
        QueueSettingsSnapshot settings,
        string? outputFolderPath,
        string? plannedOutputPath,
        bool hasExistingDirectOutput)
    {
        if (!string.IsNullOrWhiteSpace(plannedOutputPath))
        {
            item.PlannedOutputPath = plannedOutputPath;
        }

        item.OutputDestinationMode = settings.OutputDestinationMode;
        item.SelectedOutputFolder = settings.OutputDestinationMode == OutputDestinationMode.ChooseOutputFolder
            ? outputFolderPath
            : null;
        item.OutputFormat = settings.OutputFormat;
        item.ConversionMode = settings.ConversionMode;
        item.Fps = settings.Fps;
        item.HasExistingDirectOutput = hasExistingDirectOutput;
    }

    private static string FormatProbeProgressText(ProbeResult probeResult)
    {
        if (probeResult.Width.HasValue && probeResult.Height.HasValue)
        {
            return $"{probeResult.Width.Value}x{probeResult.Height.Value}";
        }

        return "Ready";
    }
}

public sealed record QueueRefreshResult(int RefreshedCount, int InvalidCount);
