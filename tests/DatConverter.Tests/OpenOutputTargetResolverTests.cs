namespace DatConverter.Tests;

public sealed class OpenOutputTargetResolverTests
{
    [Fact]
    public void Resolve_SelectedCompletedItemSelectsItsOutputFile()
    {
        using var temp = new TempDirectory();
        var item = CreateItem(temp.Path, "clip.dat", "clip.mp4", QueueItemStatus.Completed, "Completed");
        File.WriteAllText(item.PlannedOutputPath, "output");

        var target = OpenOutputTargetResolver.Resolve([item], item, null);

        Assert.Equal(OpenOutputTargetKind.SelectFile, target.Kind);
        Assert.Equal(item.PlannedOutputPath, target.Path);
        Assert.Same(item, target.QueueItem);
    }

    [Fact]
    public void Resolve_SelectedExistingOutputItemSelectsExistingOutputFile()
    {
        using var temp = new TempDirectory();
        var item = CreateItem(temp.Path, "clip.dat", "clip.mp4", QueueItemStatus.Skipped, "Exists", hasExistingDirectOutput: true);
        File.WriteAllText(item.PlannedOutputPath, "existing");

        var target = OpenOutputTargetResolver.Resolve([item], item, null);

        Assert.Equal(OpenOutputTargetKind.SelectFile, target.Kind);
        Assert.Equal(item.PlannedOutputPath, target.Path);
    }

    [Fact]
    public void Resolve_SelectedFailedItemFallsBackToPlannedOutputFolder()
    {
        using var temp = new TempDirectory();
        var item = CreateItem(temp.Path, "clip.dat", "clip.mp4", QueueItemStatus.Failed, "Failed");

        var target = OpenOutputTargetResolver.Resolve([item], item, null);

        Assert.Equal(OpenOutputTargetKind.OpenFolder, target.Kind);
        Assert.Equal(temp.Path, target.Path);
    }

    [Fact]
    public void Resolve_SelectedUnsupportedItemFallsBackToPlannedOutputFolder()
    {
        using var temp = new TempDirectory();
        var item = CreateItem(temp.Path, "helper.dat", "helper.mp4", QueueItemStatus.Unsupported, "Unsupported");

        var target = OpenOutputTargetResolver.Resolve([item], item, null);

        Assert.Equal(OpenOutputTargetKind.OpenFolder, target.Kind);
        Assert.Equal(temp.Path, target.Path);
    }

    [Fact]
    public void Resolve_NoSelectionFallsBackToFirstCompletedOrExistingItem()
    {
        using var temp = new TempDirectory();
        var failed = CreateItem(temp.Path, "failed.dat", "failed.mp4", QueueItemStatus.Failed, "Failed");
        var completed = CreateItem(temp.Path, "done.dat", "done.mp4", QueueItemStatus.Completed, "Completed");
        File.WriteAllText(completed.PlannedOutputPath, "output");

        var target = OpenOutputTargetResolver.Resolve([failed, completed], null, null);

        Assert.Equal(OpenOutputTargetKind.SelectFile, target.Kind);
        Assert.Same(completed, target.QueueItem);
        Assert.Equal(completed.PlannedOutputPath, target.Path);
    }

    [Fact]
    public void Resolve_NoSelectionUsesValidLastSuccessfulOutputBeforeQueueFallback()
    {
        using var temp = new TempDirectory();
        var completed = CreateItem(temp.Path, "done.dat", "done.mp4", QueueItemStatus.Completed, "Completed");
        File.WriteAllText(completed.PlannedOutputPath, "output");
        var lastOutputPath = Path.Combine(temp.Path, "last.mp4");
        File.WriteAllText(lastOutputPath, "last");

        var target = OpenOutputTargetResolver.Resolve([completed], null, lastOutputPath);

        Assert.Equal(OpenOutputTargetKind.SelectFile, target.Kind);
        Assert.Null(target.QueueItem);
        Assert.Equal(lastOutputPath, target.Path);
    }

    [Fact]
    public void Resolve_SelectedItemWinsOverLastSuccessfulOutput()
    {
        using var temp = new TempDirectory();
        var selected = CreateItem(temp.Path, "selected.dat", "selected.mp4", QueueItemStatus.Completed, "Completed");
        File.WriteAllText(selected.PlannedOutputPath, "selected");
        var lastOutputPath = Path.Combine(temp.Path, "last.mp4");
        File.WriteAllText(lastOutputPath, "last");

        var target = OpenOutputTargetResolver.Resolve([selected], selected, lastOutputPath);

        Assert.Equal(OpenOutputTargetKind.SelectFile, target.Kind);
        Assert.Same(selected, target.QueueItem);
        Assert.Equal(selected.PlannedOutputPath, target.Path);
    }

    [Fact]
    public void Resolve_InvalidLastSuccessfulOutputFallsBackToFirstCompletedItem()
    {
        using var temp = new TempDirectory();
        var completed = CreateItem(temp.Path, "done.dat", "done.mp4", QueueItemStatus.Completed, "Completed");
        File.WriteAllText(completed.PlannedOutputPath, "output");
        var missingLastOutputPath = Path.Combine(temp.Path, "missing-folder", "missing.mp4");

        var target = OpenOutputTargetResolver.Resolve([completed], null, missingLastOutputPath);

        Assert.Equal(OpenOutputTargetKind.SelectFile, target.Kind);
        Assert.Same(completed, target.QueueItem);
        Assert.Equal(completed.PlannedOutputPath, target.Path);
    }

    [Fact]
    public void Resolve_MultipleOutputFoldersUsesSelectedItemOnly()
    {
        using var temp = new TempDirectory();
        var folderA = Path.Combine(temp.Path, "a");
        var folderB = Path.Combine(temp.Path, "b");
        Directory.CreateDirectory(folderA);
        Directory.CreateDirectory(folderB);
        var first = CreateItem(folderA, "first.dat", "first.mp4", QueueItemStatus.Completed, "Completed");
        var second = CreateItem(folderB, "second.dat", "second.mp4", QueueItemStatus.Completed, "Completed");
        File.WriteAllText(first.PlannedOutputPath, "first");
        File.WriteAllText(second.PlannedOutputPath, "second");

        var target = OpenOutputTargetResolver.Resolve([first, second], second, null);

        Assert.Equal(OpenOutputTargetKind.SelectFile, target.Kind);
        Assert.Equal(second.PlannedOutputPath, target.Path);
        Assert.NotEqual(first.PlannedOutputPath, target.Path);
    }

    private static QueueItem CreateItem(
        string folderPath,
        string inputFileName,
        string outputFileName,
        QueueItemStatus status,
        string statusText,
        bool hasExistingDirectOutput = false)
    {
        var item = new QueueItem(
            Path.Combine(folderPath, inputFileName),
            Path.Combine(folderPath, outputFileName),
            OutputDestinationMode.SameFolderAsSource,
            null,
            OutputFormat.Mp4,
            "Fast",
            FpsOption.FromLabel("30"),
            hasExistingDirectOutput);
        item.Status = status;
        item.StatusText = statusText;
        item.ProgressText = status == QueueItemStatus.Skipped ? "Selected output exists" : "";
        return item;
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "DatConverter.Tests." + Guid.NewGuid().ToString("N"));
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
            }
        }
    }
}
