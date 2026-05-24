namespace DatConverter;

public static class QueueFpsValidationService
{
    private const int MaxListedFiles = 5;

    public static IReadOnlyList<QueueItem> FindItemsRequiringManualFps(IEnumerable<QueueItem> items)
    {
        return items
            .Where(item => item.RequiresManualFpsSelection || !item.HasResolvedFps)
            .Where(IsEligibleForQueueStartValidation)
            .ToList();
    }

    public static string BuildManualFpsRequiredMessage(IReadOnlyList<QueueItem> items)
    {
        var fileLines = items
            .Take(MaxListedFiles)
            .Select(item => $"- {Path.GetFileName(item.InputPath)}")
            .ToList();

        var intro = items.Count > MaxListedFiles
            ? $"{items.Count} files need Source FPS. Showing first {MaxListedFiles}:"
            : "Files that need Source FPS:";

        return "Some files need a Source FPS before conversion can start." +
               Environment.NewLine +
               Environment.NewLine +
               "Auto-detect could not determine the FPS for the files below. Double-click each row marked \"Needs FPS\" and choose Source FPS." +
               Environment.NewLine +
               Environment.NewLine +
               intro +
               Environment.NewLine +
               string.Join(Environment.NewLine, fileLines);
    }

    private static bool IsEligibleForQueueStartValidation(QueueItem item)
    {
        return item.Status is QueueItemStatus.WaitingForProbe
            or QueueItemStatus.Ready
            or QueueItemStatus.Warning;
    }
}
