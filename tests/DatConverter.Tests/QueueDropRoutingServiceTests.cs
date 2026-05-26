namespace DatConverter.Tests;

public sealed class QueueDropRoutingServiceTests
{
    [Fact]
    public void CreatePlan_WhenQueueStopped_RoutesFilesAndFoldersForAdd()
    {
        using var temp = new TempDirectory();
        var datPath = CreateFile(temp.Path, "clip.dat");
        var txtPath = CreateFile(temp.Path, "notes.txt");
        var folderPath = Path.Combine(temp.Path, "batch");
        Directory.CreateDirectory(folderPath);

        var plan = QueueDropRoutingService.CreatePlan([datPath, txtPath, folderPath], isQueueProcessing: false);

        Assert.Equal([datPath, txtPath], plan.FilePathsToAdd);
        Assert.Equal([folderPath], plan.FolderPathsToAdd);
        Assert.Empty(plan.RejectedFolderPaths);
        Assert.Empty(plan.MissingPaths);
    }

    [Fact]
    public void CreatePlan_WhenQueueRunning_RoutesFilesAndRejectsFolders()
    {
        using var temp = new TempDirectory();
        var datPath = CreateFile(temp.Path, "clip.dat");
        var folderPath = Path.Combine(temp.Path, "batch");
        Directory.CreateDirectory(folderPath);

        var plan = QueueDropRoutingService.CreatePlan([datPath, folderPath], isQueueProcessing: true);

        Assert.Equal([datPath], plan.FilePathsToAdd);
        Assert.Empty(plan.FolderPathsToAdd);
        Assert.Equal([folderPath], plan.RejectedFolderPaths);
        Assert.Empty(plan.MissingPaths);
    }

    [Fact]
    public void CreatePlan_KeepsInvalidFilePathsOnFileRouteForExistingAddValidation()
    {
        using var temp = new TempDirectory();
        var txtPath = CreateFile(temp.Path, "notes.txt");

        var plan = QueueDropRoutingService.CreatePlan([txtPath], isQueueProcessing: false);

        Assert.Equal([txtPath], plan.FilePathsToAdd);
        Assert.Empty(plan.FolderPathsToAdd);
        Assert.Empty(plan.RejectedFolderPaths);
    }

    [Fact]
    public void CreatePlan_MissingPathsAreIgnoredForQueueMutation()
    {
        using var temp = new TempDirectory();
        var missingPath = Path.Combine(temp.Path, "missing.dat");

        var plan = QueueDropRoutingService.CreatePlan([missingPath], isQueueProcessing: false);

        Assert.Empty(plan.FilePathsToAdd);
        Assert.Empty(plan.FolderPathsToAdd);
        Assert.Empty(plan.RejectedFolderPaths);
        Assert.Equal([missingPath], plan.MissingPaths);
        Assert.False(plan.HasDroppedItems);
    }

    private static string CreateFile(string folderPath, string fileName)
    {
        var path = Path.Combine(folderPath, fileName);
        File.WriteAllBytes(path, [0x01]);
        return path;
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dat-converter-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch
            {
                // Best-effort cleanup for temp files held briefly by test infrastructure.
            }
        }
    }
}
