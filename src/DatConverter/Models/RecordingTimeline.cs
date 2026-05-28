namespace DatConverter;

public sealed class RecordingTimeline
{
    public required string SourcePath { get; init; }

    public required IReadOnlyList<RecordingTimelineSegment> Segments { get; init; }

    public TimeSpan? TotalDuration { get; init; }

    public DateTime? RecordingStart { get; init; }

    public DateTime? RecordingEnd { get; init; }

    public bool HasRecordingTimestamps => RecordingStart.HasValue;

    public bool IsSplitRecording => Segments.Count > 1;

    public bool TryMapElapsedOffset(TimeSpan offset, out RecordingTimelinePosition? position)
    {
        position = null;
        if (offset < TimeSpan.Zero || Segments.Count == 0)
        {
            return false;
        }

        foreach (var segment in Segments)
        {
            if (!segment.Duration.HasValue)
            {
                continue;
            }

            var segmentStart = segment.ElapsedOffset;
            var segmentEnd = segmentStart + segment.Duration.Value;
            if (offset >= segmentStart && offset < segmentEnd)
            {
                position = new RecordingTimelinePosition
                {
                    Segment = segment,
                    FullRecordingOffset = offset,
                    SegmentLocalOffset = offset - segmentStart
                };
                return true;
            }
        }

        var last = Segments[^1];
        if (last.Duration.HasValue && TotalDuration.HasValue && offset == TotalDuration.Value)
        {
            position = new RecordingTimelinePosition
            {
                Segment = last,
                FullRecordingOffset = offset,
                SegmentLocalOffset = last.Duration.Value
            };
            return true;
        }

        return false;
    }
}
