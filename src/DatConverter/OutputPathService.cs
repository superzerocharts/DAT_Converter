namespace DatConverter;

public static class OutputPathService
{
    public static bool IsSafeOutputPath(string? inputFilePath, string? outputFilePath)
    {
        if (string.IsNullOrWhiteSpace(inputFilePath) || string.IsNullOrWhiteSpace(outputFilePath))
        {
            return false;
        }

        try
        {
            var inputFullPath = Path.GetFullPath(inputFilePath);
            var outputFullPath = Path.GetFullPath(outputFilePath);
            return !string.Equals(inputFullPath, outputFullPath, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return false;
        }
    }

    public static string? PlanOutputPath(string? inputFilePath, string? outputFolderPath, OutputFormat outputFormat)
    {
        if (string.IsNullOrWhiteSpace(inputFilePath) || string.IsNullOrWhiteSpace(outputFolderPath))
        {
            return null;
        }

        if (!Directory.Exists(outputFolderPath))
        {
            return null;
        }

        var inputBaseName = Path.GetFileNameWithoutExtension(inputFilePath);
        if (string.IsNullOrWhiteSpace(inputBaseName))
        {
            return null;
        }

        var extension = outputFormat.Extension();
        var baseOutputPath = Path.Combine(outputFolderPath, inputBaseName + extension);
        return IsSafeOutputPath(inputFilePath, baseOutputPath) ? baseOutputPath : null;
    }

    public static string? GetDirectOutputPath(string? inputFilePath, string? outputFolderPath, OutputFormat outputFormat)
    {
        if (string.IsNullOrWhiteSpace(inputFilePath) || string.IsNullOrWhiteSpace(outputFolderPath))
        {
            return null;
        }

        if (!Directory.Exists(outputFolderPath))
        {
            return null;
        }

        var inputBaseName = Path.GetFileNameWithoutExtension(inputFilePath);
        if (string.IsNullOrWhiteSpace(inputBaseName))
        {
            return null;
        }

        var outputPath = Path.Combine(outputFolderPath, inputBaseName + outputFormat.Extension());
        return IsSafeOutputPath(inputFilePath, outputPath) ? outputPath : null;
    }

    public static string? PlanUniqueOutputPath(
        string? inputFilePath,
        string? outputFolderPath,
        OutputFormat outputFormat,
        Func<string, bool> isCandidateAllowed,
        bool allowExistingDirectOutput = true)
    {
        var directOutputPath = GetDirectOutputPath(inputFilePath, outputFolderPath, outputFormat);
        if (string.IsNullOrWhiteSpace(directOutputPath))
        {
            return null;
        }

        if (isCandidateAllowed(directOutputPath) &&
            (allowExistingDirectOutput || !File.Exists(directOutputPath)))
        {
            return directOutputPath;
        }

        if (string.IsNullOrWhiteSpace(inputFilePath) || string.IsNullOrWhiteSpace(outputFolderPath))
        {
            return null;
        }

        var inputBaseName = Path.GetFileNameWithoutExtension(inputFilePath);
        var extension = outputFormat.Extension();
        if (string.IsNullOrWhiteSpace(inputBaseName) || string.IsNullOrWhiteSpace(extension))
        {
            return null;
        }

        for (var index = 1; index <= 999; index++)
        {
            var candidate = Path.Combine(outputFolderPath, $"{inputBaseName}_{index:00}{extension}");
            if (IsSafeOutputPath(inputFilePath, candidate) &&
                !File.Exists(candidate) &&
                isCandidateAllowed(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    public static CustomOutputPathValidationResult ValidateCustomOutputPath(
        string? inputFilePath,
        string? outputFilePath,
        OutputFormat outputFormat,
        bool requireAvailable,
        bool allowExtensionCorrection = false)
    {
        if (string.IsNullOrWhiteSpace(inputFilePath))
        {
            return CustomOutputPathValidationResult.Invalid("Source file is not valid.");
        }

        if (string.IsNullOrWhiteSpace(outputFilePath))
        {
            return CustomOutputPathValidationResult.Invalid("Save As path cannot be empty.");
        }

        string fullOutputPath;
        try
        {
            fullOutputPath = Path.GetFullPath(outputFilePath.Trim());
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return CustomOutputPathValidationResult.Invalid("Save As path is not valid.");
        }

        var directoryPath = Path.GetDirectoryName(fullOutputPath);
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            return CustomOutputPathValidationResult.Invalid("Save As path must include a folder.");
        }

        var folderValidation = OutputFolderValidator.ValidateOutputFolder(directoryPath);
        if (!folderValidation.IsValid || string.IsNullOrWhiteSpace(folderValidation.FolderPath))
        {
            return CustomOutputPathValidationResult.Invalid(folderValidation.Message);
        }

        var fileName = Path.GetFileName(fullOutputPath);
        if (!IsValidOutputFileName(fileName))
        {
            return CustomOutputPathValidationResult.Invalid("Save As path must include a valid file name.");
        }

        var requiredExtension = outputFormat.Extension();
        var currentExtension = Path.GetExtension(fullOutputPath);
        if (string.IsNullOrWhiteSpace(currentExtension))
        {
            fullOutputPath += requiredExtension;
        }
        else if (!string.Equals(currentExtension, requiredExtension, StringComparison.OrdinalIgnoreCase))
        {
            if (!allowExtensionCorrection)
            {
                return CustomOutputPathValidationResult.Invalid($"Save As path must use the {requiredExtension} extension.");
            }

            fullOutputPath = Path.ChangeExtension(fullOutputPath, requiredExtension);
        }

        if (!IsSafeOutputPath(inputFilePath, fullOutputPath))
        {
            return CustomOutputPathValidationResult.Invalid("Save As path cannot match the source file.");
        }

        var outputExists = File.Exists(fullOutputPath);
        if (requireAvailable && outputExists)
        {
            return CustomOutputPathValidationResult.Invalid("A file by that name already exists.");
        }

        return CustomOutputPathValidationResult.Valid(fullOutputPath, outputExists);
    }

    private static bool IsValidOutputFileName(string? outputFileName)
    {
        if (string.IsNullOrWhiteSpace(outputFileName))
        {
            return false;
        }

        return outputFileName.Trim().Length > 0 &&
               outputFileName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 &&
               !outputFileName.EndsWith(' ') &&
               !outputFileName.EndsWith('.');
    }
}

public sealed record CustomOutputPathValidationResult(
    bool IsValid,
    string? OutputPath,
    bool OutputExists,
    string Message)
{
    public static CustomOutputPathValidationResult Valid(string outputPath, bool outputExists)
    {
        return new CustomOutputPathValidationResult(true, outputPath, outputExists, "Save As path is valid.");
    }

    public static CustomOutputPathValidationResult Invalid(string message)
    {
        return new CustomOutputPathValidationResult(false, null, false, message);
    }
}
