namespace DatConverter;

public sealed record InputFileValidationResult(
    bool IsValid,
    string? FilePath,
    string Message,
    long FileSizeBytes = 0);
