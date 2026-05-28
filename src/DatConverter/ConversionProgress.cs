namespace DatConverter;

public sealed record ConversionProgress(
    int? Percent,
    TimeSpan? OutputTime,
    string? Frame,
    string? Speed,
    bool IsEnd,
    string Summary,
    string? Fps = null,
    string? Bitrate = null,
    string? TotalSize = null,
    string? DupFrames = null,
    string? DropFrames = null,
    string? OutTimeUs = null,
    string? OutTimeMs = null);
