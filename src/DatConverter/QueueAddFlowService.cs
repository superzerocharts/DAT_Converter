namespace DatConverter;

public enum FinishedQueueAddChoice
{
    Cancel,
    ClearAndAdd,
    KeepAndAdd
}

public static class QueueAddFlowService
{
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
            or QueueItemStatus.Canceled;
    }
}
