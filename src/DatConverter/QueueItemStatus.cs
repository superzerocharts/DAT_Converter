namespace DatConverter;

public enum QueueItemStatus
{
    WaitingForProbe,
    Ready,
    Warning,
    Probing,
    Converting,
    Unsupported,
    Invalid,
    Skipped,
    Running,
    Completed,
    Failed,
    Canceled
}
