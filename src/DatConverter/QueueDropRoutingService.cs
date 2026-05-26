namespace DatConverter;

public sealed record QueueDropRoutingResult(
    IReadOnlyList<string> FilePaths,
    IReadOnlyList<string> FolderPaths,
    IReadOnlyList<string> MissingPaths)
{
    public bool HasDroppedItems => FilePaths.Count > 0 || FolderPaths.Count > 0;
}

public sealed record QueueDropPlan(
    IReadOnlyList<string> FilePathsToAdd,
    IReadOnlyList<string> FolderPathsToAdd,
    IReadOnlyList<string> RejectedFolderPaths,
    IReadOnlyList<string> MissingPaths)
{
    public bool HasDroppedItems => FilePathsToAdd.Count > 0 || FolderPathsToAdd.Count > 0 || RejectedFolderPaths.Count > 0;
}

public static class QueueDropRoutingService
{
    public static QueueDropPlan CreatePlan(IEnumerable<string> paths, bool isQueueProcessing)
    {
        var routing = ClassifyPaths(paths);
        return new QueueDropPlan(
            routing.FilePaths,
            isQueueProcessing ? Array.Empty<string>() : routing.FolderPaths,
            isQueueProcessing ? routing.FolderPaths : Array.Empty<string>(),
            routing.MissingPaths);
    }

    public static QueueDropRoutingResult ClassifyPaths(IEnumerable<string> paths)
    {
        var filePaths = new List<string>();
        var folderPaths = new List<string>();
        var missingPaths = new List<string>();

        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            if (Directory.Exists(path))
            {
                folderPaths.Add(path);
                continue;
            }

            if (File.Exists(path))
            {
                filePaths.Add(path);
                continue;
            }

            missingPaths.Add(path);
        }

        return new QueueDropRoutingResult(filePaths, folderPaths, missingPaths);
    }
}
