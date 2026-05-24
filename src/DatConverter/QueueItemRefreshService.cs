namespace DatConverter;

public static class QueueItemRefreshService
{
    public static QueueRefreshResult RefreshEditableItems(
        IEnumerable<QueueItem> items,
        QueueSettingsSnapshot settings,
        Func<QueueItem, QueueSettingsSnapshot, string?> resolveOutputFolder,
        Func<QueueItem, string, OutputFormat, string?> planOutputPath,
        Func<QueueItem, string, OutputFormat, string?> getDirectOutputPath,
        Func<QueueItem, QueueSettingsSnapshot, QueueItemFpsResolution>? resolveFps = null)
    {
        var refreshed = 0;
        var invalid = 0;

        foreach (var item in items.Where(CanRefreshFromLiveSettings))
        {
            var itemOutputFormat = item.HasCustomFormat ? item.OutputFormat : settings.OutputFormat;
            var itemConversionMode = item.HasCustomMode ? item.ConversionMode : settings.ConversionMode;
            var itemFpsSettings = item.HasCustomFpsSetting ? item.FpsSettings : settings.FpsSettings;
            var itemSettings = settings with
            {
                OutputFormat = itemOutputFormat,
                ConversionMode = itemConversionMode,
                Fps = itemFpsSettings.ToManualFpsOption(),
                FpsSettings = itemFpsSettings
            };

            var outputFolderPath = resolveOutputFolder(item, itemSettings);
            var outputFolderValidation = OutputFolderValidator.ValidateOutputFolder(outputFolderPath);
            if (!outputFolderValidation.IsValid || string.IsNullOrWhiteSpace(outputFolderValidation.FolderPath))
            {
                ApplySettings(item, itemSettings, null, null, hasExistingDirectOutput: false, ResolveFps(item, itemSettings, resolveFps));
                item.Status = QueueItemStatus.Invalid;
                item.StatusText = "Output invalid";
                item.ProgressText = outputFolderValidation.Message;
                item.PreProbeResult = null;
                invalid++;
                continue;
            }

            var directOutputPath = getDirectOutputPath(item, outputFolderValidation.FolderPath, itemSettings.OutputFormat);
            var hasExistingDirectOutput = !string.IsNullOrWhiteSpace(directOutputPath) && File.Exists(directOutputPath);
            var plannedOutputPath = planOutputPath(item, outputFolderValidation.FolderPath, itemSettings.OutputFormat);
            var hasExistingPlannedOutput = hasExistingDirectOutput &&
                                           !string.IsNullOrWhiteSpace(plannedOutputPath) &&
                                           string.Equals(directOutputPath, plannedOutputPath, StringComparison.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(plannedOutputPath))
            {
                ApplySettings(item, itemSettings, outputFolderValidation.FolderPath, null, hasExistingDirectOutput, ResolveFps(item, itemSettings, resolveFps));
                item.Status = QueueItemStatus.Invalid;
                item.StatusText = "No output";
                item.ProgressText = "No safe output path";
                item.PreProbeResult = null;
                invalid++;
                continue;
            }

            var fpsResolution = ResolveFps(item, itemSettings, resolveFps);
            var wasProbeValidForSettings =
                item.PreProbeResult?.IsSuccess == true &&
                string.Equals(item.Fps.FfmpegValue, fpsResolution.FfmpegRateValue, StringComparison.Ordinal);
            var readyProgressText = wasProbeValidForSettings && item.PreProbeResult is not null
                ? FormatProbeProgressText(item.PreProbeResult)
                : QueueItemStatusText.CheckingFile;

            ApplySettings(item, itemSettings, outputFolderValidation.FolderPath, plannedOutputPath, hasExistingPlannedOutput, fpsResolution);

            if (hasExistingPlannedOutput)
            {
                item.Status = QueueItemStatus.Skipped;
                item.StatusText = "Exists";
                item.ProgressText = "Selected output exists";
                item.ResultStatusSummary = "Skipped - output already exists";
                refreshed++;
                continue;
            }

            if (!fpsResolution.HasResolvedFps)
            {
                item.PreProbeResult = null;
                item.Status = QueueItemStatus.Warning;
                item.StatusText = "Needs FPS";
                item.ProgressText = "Choose Source FPS";
                refreshed++;
                continue;
            }

            if (!wasProbeValidForSettings)
            {
                item.PreProbeResult = null;
                item.Status = QueueItemStatus.WaitingForProbe;
                item.StatusText = QueueItemStatusText.CheckingFile;
                item.ProgressText = "";
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
        bool hasExistingDirectOutput,
        QueueItemFpsResolution fpsResolution)
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
        item.ApplyFpsResolution(settings.FpsSettings, fpsResolution);
        item.HasExistingDirectOutput = hasExistingDirectOutput;
    }

    private static QueueItemFpsResolution ResolveFps(
        QueueItem item,
        QueueSettingsSnapshot settings,
        Func<QueueItem, QueueSettingsSnapshot, QueueItemFpsResolution>? resolveFps)
    {
        return resolveFps?.Invoke(item, settings)
            ?? QueueItemFpsResolution.FromManual(settings.FpsSettings.ToManualFpsOption());
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
