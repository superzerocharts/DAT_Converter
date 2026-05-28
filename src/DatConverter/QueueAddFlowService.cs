namespace DatConverter;

public static class QueueAddFlowService
{
    public static bool ShouldAutoClearBeforeAdd(IEnumerable<QueueItem> items, bool isQueueProcessing)
    {
        return !isQueueProcessing && HasOnlyFinishedItems(items);
    }

    public static QueueSettingsSnapshot CreateDefaultBatchOptionsAfterAutoClear()
    {
        return new QueueSettingsSnapshot(
            OutputFormat.Mp4,
            "Remux",
            FpsOption.FromLabel("30"),
            OutputDestinationMode.SameFolderAsSource,
            null)
        {
            FpsSettings = QueueItemFpsSettings.AutoDetect()
        };
    }

    public static bool HasOnlyFinishedItems(IEnumerable<QueueItem> items)
    {
        var any = false;
        foreach (var item in items)
        {
            any = true;
            if (!IsFinishedStatus(item.Status))
            {
                return false;
            }
        }

        return any;
    }

    public static bool IsFinishedStatus(QueueItemStatus status)
    {
        return status is QueueItemStatus.Completed
            or QueueItemStatus.Skipped
            or QueueItemStatus.Failed
            or QueueItemStatus.Canceled
            or QueueItemStatus.Unsupported
            or QueueItemStatus.Invalid;
    }
}
