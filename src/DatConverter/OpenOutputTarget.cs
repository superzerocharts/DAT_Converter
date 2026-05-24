namespace DatConverter;

public enum OpenOutputTargetKind
{
    SelectFile,
    OpenFolder,
    Unavailable
}

public sealed record OpenOutputTarget(
    OpenOutputTargetKind Kind,
    string? Path,
    QueueItem? QueueItem,
    string Message);
