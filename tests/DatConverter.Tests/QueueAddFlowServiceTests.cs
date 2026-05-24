namespace DatConverter.Tests;

public sealed class QueueAddFlowServiceTests
{
    [Fact]
    public void HasOnlyFinishedItems_ReturnsFalseForEmptyQueue()
    {
        Assert.False(QueueAddFlowService.HasOnlyFinishedItems(Array.Empty<QueueItem>()));
    }

    [Theory]
    [InlineData(QueueItemStatus.Completed)]
    [InlineData(QueueItemStatus.Skipped)]
    [InlineData(QueueItemStatus.Failed)]
    [InlineData(QueueItemStatus.Canceled)]
    public void IsFinishedStatus_IncludesCompletedExistsFailedCanceled(QueueItemStatus status)
    {
        Assert.True(QueueAddFlowService.IsFinishedStatus(status));
    }

    [Theory]
    [InlineData(QueueItemStatus.Ready)]
    [InlineData(QueueItemStatus.Warning)]
    [InlineData(QueueItemStatus.WaitingForProbe)]
    [InlineData(QueueItemStatus.Invalid)]
    [InlineData(QueueItemStatus.Unsupported)]
    [InlineData(QueueItemStatus.Probing)]
    [InlineData(QueueItemStatus.Converting)]
    public void IsFinishedStatus_ExcludesActionableOrRunningItems(QueueItemStatus status)
    {
        Assert.False(QueueAddFlowService.IsFinishedStatus(status));
    }

    [Fact]
    public void HasOnlyFinishedItems_ReturnsTrueWhenEveryRowIsFinished()
    {
        var items = new[]
        {
            CreateItem(QueueItemStatus.Completed),
            CreateItem(QueueItemStatus.Skipped),
            CreateItem(QueueItemStatus.Failed),
            CreateItem(QueueItemStatus.Canceled)
        };

        Assert.True(QueueAddFlowService.HasOnlyFinishedItems(items));
    }

    [Theory]
    [InlineData(QueueItemStatus.Ready)]
    [InlineData(QueueItemStatus.Warning)]
    [InlineData(QueueItemStatus.WaitingForProbe)]
    [InlineData(QueueItemStatus.Invalid)]
    public void HasOnlyFinishedItems_ReturnsFalseWhenAnyRowIsActionable(QueueItemStatus actionableStatus)
    {
        var items = new[]
        {
            CreateItem(QueueItemStatus.Completed),
            CreateItem(actionableStatus)
        };

        Assert.False(QueueAddFlowService.HasOnlyFinishedItems(items));
    }

    private static QueueItem CreateItem(QueueItemStatus status)
    {
        return new QueueItem(
            @"C:\input\clip.dat",
            @"C:\input\clip.mp4",
            OutputDestinationMode.SameFolderAsSource,
            null,
            OutputFormat.Mp4,
            "Remux",
            FpsOption.FromLabel("30"),
            hasExistingDirectOutput: status == QueueItemStatus.Skipped)
        {
            Status = status,
            StatusText = status.ToString()
        };
    }
}
