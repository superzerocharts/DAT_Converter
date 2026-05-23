namespace DatConverter;

public sealed record OutputFolderValidationResult(
    bool IsValid,
    string? FolderPath,
    string Message);
