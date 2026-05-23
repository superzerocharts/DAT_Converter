namespace DatConverter;

public static class InputFileValidator
{
    public static InputFileValidationResult ValidateDatFile(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return new InputFileValidationResult(false, filePath, "Select a .dat file.");
        }

        if (!File.Exists(filePath))
        {
            return new InputFileValidationResult(false, filePath, "The selected .dat file does not exist.");
        }

        if (!string.Equals(Path.GetExtension(filePath), ".dat", StringComparison.OrdinalIgnoreCase))
        {
            return new InputFileValidationResult(false, filePath, "Only compatible raw H.264 .dat files are supported.");
        }

        FileInfo fileInfo;
        try
        {
            fileInfo = new FileInfo(filePath);
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return new InputFileValidationResult(false, filePath, "The selected file path is not valid.");
        }

        if (fileInfo.Length <= 0)
        {
            return new InputFileValidationResult(false, filePath, "The selected .dat file is empty.");
        }

        try
        {
            using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            _ = stream.CanRead;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return new InputFileValidationResult(false, filePath, "The selected .dat file could not be read.");
        }

        return new InputFileValidationResult(true, filePath, "Input file is valid.", fileInfo.Length);
    }
}
