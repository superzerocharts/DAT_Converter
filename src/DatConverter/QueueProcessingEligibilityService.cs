namespace DatConverter;

public static class QueueProcessingEligibilityService
{
    public static bool IsProcessable(QueueItem item, Func<QueueItem, bool>? customOutputPathExists = null)
    {
        if (item.RequiresManualFpsSelection || !item.HasResolvedFps)
        {
            return false;
        }

        if (item.Status == QueueItemStatus.Ready ||
            item.Status == QueueItemStatus.WaitingForProbe)
        {
            return true;
        }

        return item.Status == QueueItemStatus.Warning &&
               item.PreProbeResult?.IsSuccess == true &&
               customOutputPathExists?.Invoke(item) != true;
    }
}
