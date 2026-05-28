namespace DatConverter;

public sealed record DatPreviewFrameRecord(
    long MarkerOffset,
    string MarkerKind,
    ulong Timestamp,
    uint PayloadSize,
    TimeSpan LocalOffset);
