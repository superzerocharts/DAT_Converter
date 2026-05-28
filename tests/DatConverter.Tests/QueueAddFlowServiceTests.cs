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
    [InlineData(QueueItemStatus.Unsupported)]
    [InlineData(QueueItemStatus.Invalid)]
    public void IsFinishedStatus_IncludesCompletedExistsFailedCanceled(QueueItemStatus status)
    {
        Assert.True(QueueAddFlowService.IsFinishedStatus(status));
    }

    [Theory]
    [InlineData(QueueItemStatus.Ready)]
    [InlineData(QueueItemStatus.Warning)]
    [InlineData(QueueItemStatus.WaitingForProbe)]
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
            CreateItem(QueueItemStatus.Canceled),
            CreateItem(QueueItemStatus.Unsupported),
            CreateItem(QueueItemStatus.Invalid)
        };

        Assert.True(QueueAddFlowService.HasOnlyFinishedItems(items));
    }

    [Theory]
    [InlineData(QueueItemStatus.Ready)]
    [InlineData(QueueItemStatus.Warning)]
    [InlineData(QueueItemStatus.WaitingForProbe)]
    public void HasOnlyFinishedItems_ReturnsFalseWhenAnyRowIsActionable(QueueItemStatus actionableStatus)
    {
        var items = new[]
        {
            CreateItem(QueueItemStatus.Completed),
            CreateItem(actionableStatus)
        };

        Assert.False(QueueAddFlowService.HasOnlyFinishedItems(items));
    }

    [Fact]
    public void ShouldAutoClearBeforeAdd_ReturnsTrueForFinishedIdleQueue()
    {
        var items = new[] { CreateItem(QueueItemStatus.Completed), CreateItem(QueueItemStatus.Failed) };

        Assert.True(QueueAddFlowService.ShouldAutoClearBeforeAdd(items, isQueueProcessing: false));
    }

    [Fact]
    public void ShouldAutoClearBeforeAdd_ReturnsFalseForRunningQueue()
    {
        var items = new[] { CreateItem(QueueItemStatus.Completed), CreateItem(QueueItemStatus.Failed) };

        Assert.False(QueueAddFlowService.ShouldAutoClearBeforeAdd(items, isQueueProcessing: true));
    }

    [Fact]
    public void ShouldAutoClearBeforeAdd_ReturnsFalseWhenPendingRowsRemain()
    {
        var items = new[] { CreateItem(QueueItemStatus.Completed), CreateItem(QueueItemStatus.Ready) };

        Assert.False(QueueAddFlowService.ShouldAutoClearBeforeAdd(items, isQueueProcessing: false));
    }

    [Fact]
    public void CreateDefaultBatchOptionsAfterAutoClear_ResetsToFreshQueueDefaults()
    {
        var defaults = QueueAddFlowService.CreateDefaultBatchOptionsAfterAutoClear();

        Assert.Equal(OutputFormat.Mp4, defaults.OutputFormat);
        Assert.Equal("Remux", defaults.ConversionMode);
        Assert.Equal(OutputDestinationMode.SameFolderAsSource, defaults.OutputDestinationMode);
        Assert.Null(defaults.ChosenOutputFolder);
        Assert.Equal(FpsSelectionMode.AutoDetect, defaults.FpsSettings.SelectionMode);
        Assert.Equal("Auto-detect", defaults.FpsSettings.RequestedDisplayValue);
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
