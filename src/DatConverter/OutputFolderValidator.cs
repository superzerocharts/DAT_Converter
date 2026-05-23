namespace DatConverter;

public static class OutputFolderValidator
{
    public static OutputFolderValidationResult ValidateOutputFolder(string? folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return new OutputFolderValidationResult(false, folderPath, "Select an output folder.");
        }

        if (!Directory.Exists(folderPath))
        {
            return new OutputFolderValidationResult(false, folderPath, "The selected output folder does not exist.");
        }

        try
        {
            var testFilePath = Path.Combine(folderPath, $".dat-converter-{Guid.NewGuid():N}.tmp");
            using (new FileStream(testFilePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose))
            {
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or ArgumentException or PathTooLongException or NotSupportedException)
        {
            return new OutputFolderValidationResult(false, folderPath, "The selected output folder is not writable.");
        }

        return new OutputFolderValidationResult(true, folderPath, "Output folder is valid.");
    }
}
