namespace DatConverter;

public static class FolderScanService
{
    public static FolderScanResult ScanForDatFiles(string folderPath, bool includeSubfolders, int hardLimit)
    {
        var datFiles = new List<string>();
        var skippedPaths = new List<string>();
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(folderPath))
        {
            errors.Add("No folder was selected.");
            return new FolderScanResult(datFiles, false, skippedPaths, errors);
        }

        if (!Directory.Exists(folderPath))
        {
            errors.Add($"Folder does not exist: {folderPath}");
            return new FolderScanResult(datFiles, false, skippedPaths, errors);
        }

        if (!includeSubfolders)
        {
            ScanOneFolder(folderPath, datFiles, skippedPaths, errors, hardLimit);
            return new FolderScanResult(datFiles, datFiles.Count > hardLimit, skippedPaths, errors);
        }

        var pendingFolders = new Stack<string>();
        pendingFolders.Push(folderPath);

        while (pendingFolders.Count > 0)
        {
            var currentFolder = pendingFolders.Pop();
            if (IsReparsePoint(currentFolder, skippedPaths, errors))
            {
                continue;
            }

            ScanOneFolder(currentFolder, datFiles, skippedPaths, errors, hardLimit);
            if (datFiles.Count > hardLimit)
            {
                break;
            }

            foreach (var childFolder in EnumerateDirectoriesSafely(currentFolder, skippedPaths, errors))
            {
                pendingFolders.Push(childFolder);
            }
        }

        return new FolderScanResult(datFiles, datFiles.Count > hardLimit, skippedPaths, errors);
    }

    private static void ScanOneFolder(
        string folderPath,
        List<string> datFiles,
        List<string> skippedPaths,
        List<string> errors,
        int hardLimit)
    {
        IEnumerator<string>? enumerator = null;
        try
        {
            enumerator = Directory.EnumerateFiles(folderPath, "*.dat", SearchOption.TopDirectoryOnly).GetEnumerator();
            while (enumerator.MoveNext())
            {
                var filePath = enumerator.Current;
                if (!string.Equals(Path.GetExtension(filePath), ".dat", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                datFiles.Add(filePath);
                if (datFiles.Count > hardLimit)
                {
                    return;
                }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PathTooLongException or DirectoryNotFoundException)
        {
            skippedPaths.Add(folderPath);
            errors.Add($"Skipped folder while scanning files: {folderPath}; {ex.Message}");
        }
        finally
        {
            enumerator?.Dispose();
        }
    }

    private static IEnumerable<string> EnumerateDirectoriesSafely(string folderPath, List<string> skippedPaths, List<string> errors)
    {
        try
        {
            return Directory.EnumerateDirectories(folderPath).ToList();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PathTooLongException or DirectoryNotFoundException)
        {
            skippedPaths.Add(folderPath);
            errors.Add($"Skipped folder while scanning subfolders: {folderPath}; {ex.Message}");
            return Array.Empty<string>();
        }
    }

    private static bool IsReparsePoint(string folderPath, List<string> skippedPaths, List<string> errors)
    {
        try
        {
            var attributes = File.GetAttributes(folderPath);
            if (!attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                return false;
            }

            skippedPaths.Add(folderPath);
            errors.Add($"Skipped reparse point folder: {folderPath}");
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PathTooLongException or DirectoryNotFoundException)
        {
            skippedPaths.Add(folderPath);
            errors.Add($"Skipped folder while reading attributes: {folderPath}; {ex.Message}");
            return true;
        }
    }
}
