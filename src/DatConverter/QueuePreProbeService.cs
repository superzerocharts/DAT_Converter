namespace DatConverter;

public static class QueuePreProbeService
{
    public static bool ShouldPreProbe(QueueItem item)
    {
        return item.Status == QueueItemStatus.WaitingForProbe ||
               (item.Status == QueueItemStatus.Warning &&
                item.RequiresManualFpsSelection &&
                item.PreProbeResult is null);
    }
}
