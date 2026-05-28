namespace DatConverter;

public sealed class TrimPreviewState
{
    public TrimPreviewState(RecordingTimeline timeline, TrimRange? initialTrim)
    {
        Timeline = timeline;
        Start = initialTrim?.Start;
        End = initialTrim?.End;
        Current = initialTrim?.Start ?? TimeSpan.Zero;
    }

    public RecordingTimeline Timeline { get; }

    public TimeSpan Current { get; private set; }

    public TimeSpan? Start { get; private set; }

    public TimeSpan? End { get; private set; }

    public void SetCurrent(TimeSpan value)
    {
        Current = ClampToTimeline(value);
    }

    public void SetStartToCurrent()
    {
        Start = Current;
    }

    public void SetEndToCurrent()
    {
        End = Current;
    }

    public void ClearTrim()
    {
        Start = null;
        End = null;
    }

    public bool TryBuildTrimRange(out TrimRange? range, out string? message)
    {
        range = null;
        if (!Start.HasValue && !End.HasValue)
        {
            message = null;
            return true;
        }

        if (!Start.HasValue || !End.HasValue)
        {
            message = "Start and End are required.";
            return false;
        }

        var candidate = new TrimRange(Start.Value, End.Value);
        if (!candidate.TryValidate(Timeline.TotalDuration, out message))
        {
            return false;
        }

        range = candidate;
        return true;
    }

    private TimeSpan ClampToTimeline(TimeSpan value)
    {
        if (value < TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        return Timeline.TotalDuration.HasValue && value > Timeline.TotalDuration.Value
            ? Timeline.TotalDuration.Value
            : value;
    }
}
