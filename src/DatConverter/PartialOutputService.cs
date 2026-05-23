namespace DatConverter;

public static class PartialOutputService
{
    public static string TryMovePartialOutput(string outputPath, string? protectedSourcePath = null)
    {
        if (!string.IsNullOrWhiteSpace(protectedSourcePath) &&
            !OutputPathService.IsSafeOutputPath(protectedSourcePath, outputPath))
        {
            return "Partial output cleanup was skipped because the output path matched the source path.";
        }

        if (!File.Exists(outputPath))
        {
            return "No partial output file was created.";
        }

        var partialPath = GetAvailablePartialPath(outputPath);

        try
        {
            File.Move(outputPath, partialPath);
            return $"Partial output was renamed to: {partialPath}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or PathTooLongException or NotSupportedException)
        {
            return $"Partial output could not be renamed: {ex.Message}";
        }
    }

    public static string GetAvailablePartialPath(string outputPath)
    {
        var candidatePath = outputPath + ".partial";
        if (!File.Exists(candidatePath))
        {
            return candidatePath;
        }

        for (var index = 1; index <= 9999; index++)
        {
            candidatePath = $"{outputPath}.{index:00}.partial";
            if (!File.Exists(candidatePath))
            {
                return candidatePath;
            }
        }

        return $"{outputPath}.{DateTime.Now:yyyyMMddHHmmss}.partial";
    }
}
