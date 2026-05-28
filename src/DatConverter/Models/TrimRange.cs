namespace DatConverter;

public sealed record TrimRange(TimeSpan Start, TimeSpan End)
{
    public bool IsValid(TimeSpan? knownTotalDuration = null)
    {
        return TryValidate(knownTotalDuration, out _);
    }

    public bool TryValidate(TimeSpan? knownTotalDuration, out string? message)
    {
        if (Start < TimeSpan.Zero)
        {
            message = "Start must be at or after 0.";
            return false;
        }

        if (End <= Start)
        {
            message = "End must be after Start.";
            return false;
        }

        if (knownTotalDuration.HasValue && End > knownTotalDuration.Value)
        {
            message = "End must not exceed the known video duration.";
            return false;
        }

        message = null;
        return true;
    }
}
