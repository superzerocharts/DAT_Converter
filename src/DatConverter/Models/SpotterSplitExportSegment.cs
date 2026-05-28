namespace DatConverter;

public sealed class SpotterSplitExportSegment
{
    public int SegmentNumber { get; init; }

    public string FileName { get; init; } = "";

    public string FilePath { get; init; } = "";

    public DateTime? StartTime { get; init; }

    public DateTime? EndTime { get; init; }

    public TimeSpan? Duration => StartTime.HasValue && EndTime.HasValue && EndTime.Value >= StartTime.Value
        ? EndTime.Value - StartTime.Value
        : null;

    public TimeSpan? GapFromPrevious { get; init; }
}
