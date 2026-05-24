namespace DatConverter;

public static class PartialOutputService
{
    public static string TryDeleteCanceledOutput(string outputPath, string? protectedSourcePath = null, bool deleteSidecarPartial = true)
    {
        if (!string.IsNullOrWhiteSpace(protectedSourcePath) &&
            !OutputPathService.IsSafeOutputPath(protectedSourcePath, outputPath))
        {
            return "Canceled output cleanup was skipped because the output path matched the source path.";
        }

        var messages = new List<string>();
        TryDeletePath(outputPath, "canceled output", messages);

        if (deleteSidecarPartial)
        {
            TryDeletePath(outputPath + ".partial", "canceled partial output", messages);
        }

        return messages.Count == 0
            ? "No canceled output file was created."
            : string.Join(" ", messages);
    }

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

    private static void TryDeletePath(string path, string description, ICollection<string> messages)
    {
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
            messages.Add($"{ToSentenceCase(description)} was deleted.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or PathTooLongException or NotSupportedException)
        {
            messages.Add($"{ToSentenceCase(description)} could not be deleted: {ex.Message}");
        }
    }

    private static string ToSentenceCase(string text)
    {
        return string.IsNullOrEmpty(text)
            ? text
            : char.ToUpperInvariant(text[0]) + text[1..];
    }
}
