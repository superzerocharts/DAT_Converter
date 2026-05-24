namespace DatConverter;

public static class QueueRunningValidationService
{
    public static bool ShouldProbeBeforeContinuing(QueueItem item)
    {
        return QueuePreProbeService.ShouldPreProbe(item) &&
               !QueueProcessingEligibilityService.IsProcessable(item);
    }
}
