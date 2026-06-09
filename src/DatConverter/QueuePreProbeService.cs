namespace DatConverter;

public static class QueuePreProbeService
{
    public static bool ShouldPreProbe(QueueItem item)
    {
        if (item.IsCombinedStoryboard)
        {
            return false;
        }

        return item.Status == QueueItemStatus.WaitingForProbe ||
               (item.Status == QueueItemStatus.Warning &&
                item.RequiresManualFpsSelection &&
                item.PreProbeResult is null);
    }
}
