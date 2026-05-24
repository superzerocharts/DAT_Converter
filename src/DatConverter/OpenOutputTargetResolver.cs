namespace DatConverter;

public static class OpenOutputTargetResolver
{
    public static OpenOutputTarget Resolve(
        IReadOnlyList<QueueItem> queueItems,
        QueueItem? selectedItem,
        string? lastSuccessfulOutputPath)
    {
        if (selectedItem is not null)
        {
            return ResolveQueueItem(selectedItem);
        }

        var lastSuccessfulTarget = ResolvePath(lastSuccessfulOutputPath, null);
        if (lastSuccessfulTarget.Kind == OpenOutputTargetKind.SelectFile)
        {
            return lastSuccessfulTarget;
        }

        var fallbackItem = queueItems.FirstOrDefault(IsOpenOutputFallbackItem);
        if (fallbackItem is not null)
        {
            return ResolveQueueItem(fallbackItem);
        }

        return lastSuccessfulTarget;
    }

    private static bool IsOpenOutputFallbackItem(QueueItem item)
    {
        return item.Status == QueueItemStatus.Completed ||
               item.Status == QueueItemStatus.Skipped && IsExistingOutputItem(item);
    }

    private static OpenOutputTarget ResolveQueueItem(QueueItem item)
    {
        return ResolvePath(GetBestOutputPath(item), item);
    }

    private static string? GetBestOutputPath(QueueItem item)
    {
        return item.ConversionResult?.IsSuccess == true
            ? item.ConversionResult.OutputPath
            : item.PlannedOutputPath;
    }

    private static OpenOutputTarget ResolvePath(string? outputPath, QueueItem? item)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return new OpenOutputTarget(OpenOutputTargetKind.Unavailable, null, item, "No output location is available.");
        }

        if (File.Exists(outputPath))
        {
            return new OpenOutputTarget(OpenOutputTargetKind.SelectFile, outputPath, item, "Opening output file.");
        }

        var folderPath = Directory.Exists(outputPath)
            ? outputPath
            : Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(folderPath) && Directory.Exists(folderPath))
        {
            return new OpenOutputTarget(OpenOutputTargetKind.OpenFolder, folderPath, item, "Opening output folder.");
        }

        return new OpenOutputTarget(OpenOutputTargetKind.Unavailable, outputPath, item, "Could not open the output location.");
    }

    private static bool IsExistingOutputItem(QueueItem item)
    {
        return item.HasExistingDirectOutput ||
               string.Equals(item.StatusText, "Exists", StringComparison.OrdinalIgnoreCase) ||
               item.ProgressText.Contains("exists", StringComparison.OrdinalIgnoreCase);
    }
}
