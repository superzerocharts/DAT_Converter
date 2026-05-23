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
        if (IsSafeOutputPath(inputFilePath, baseOutputPath) && !File.Exists(baseOutputPath))
        {
            return baseOutputPath;
        }

        var convertedPath = Path.Combine(outputFolderPath, inputBaseName + "_converted" + extension);
        if (IsSafeOutputPath(inputFilePath, convertedPath) && !File.Exists(convertedPath))
        {
            return convertedPath;
        }

        for (var index = 1; index <= 9999; index++)
        {
            var candidatePath = Path.Combine(outputFolderPath, $"{inputBaseName}_{index:00}{extension}");
            if (IsSafeOutputPath(inputFilePath, candidatePath) && !File.Exists(candidatePath))
            {
                return candidatePath;
            }
        }

        return null;
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
}
