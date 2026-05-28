namespace DatConverter;

public sealed class RecordingTimelineSegment
{
    public required string SourcePath { get; init; }

    public string FileName => Path.GetFileName(SourcePath);

    public DateTime? RecordingStart { get; init; }

    public DateTime? RecordingEnd { get; init; }

    public TimeSpan? Duration { get; init; }

    public TimeSpan ElapsedOffset { get; init; }
}
