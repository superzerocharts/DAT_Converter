namespace DatConverter;

public sealed record BurnTimestampOptions(
    string CameraName,
    DateTime StartTime,
    string? FontFilePath = null,
    string? FontWarning = null);
