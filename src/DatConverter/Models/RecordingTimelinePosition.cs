namespace DatConverter;

public sealed class RecordingTimelinePosition
{
    public required RecordingTimelineSegment Segment { get; init; }

    public TimeSpan FullRecordingOffset { get; init; }

    public TimeSpan SegmentLocalOffset { get; init; }
}
