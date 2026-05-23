namespace DatConverter;

public sealed record ConversionProgress(
    int? Percent,
    TimeSpan? OutputTime,
    string? Frame,
    string? Speed,
    bool IsEnd,
    string Summary);
