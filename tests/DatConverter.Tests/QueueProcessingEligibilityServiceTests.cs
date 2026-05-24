namespace DatConverter.Tests;

public sealed class QueueProcessingEligibilityServiceTests
{
    [Fact]
    public void IsProcessable_IncludesResolvedWaitingForProbeItem()
    {
        var item = CreateItem();
        item.Status = QueueItemStatus.WaitingForProbe;

        Assert.True(QueueProcessingEligibilityService.IsProcessable(item));
    }

    [Fact]
    public void IsProcessable_ExcludesUnresolvedWaitingForProbeItem()
    {
        var item = CreateItem();
        item.Status = QueueItemStatus.WaitingForProbe;
        item.ApplyFpsResolution(QueueItemFpsSettings.AutoDetect(), new QueueItemFpsResolution
        {
            SelectionMode = FpsSelectionMode.AutoDetect,
            DisplayLabel = "Needs manual selection",
            FfmpegRateValue = "",
            HasResolvedFps = false,
            RequiresManualFpsSelection = true,
            Confidence = "Unavailable"
        });

        Assert.False(QueueProcessingEligibilityService.IsProcessable(item));
    }

    [Fact]
    public void IsProcessable_IncludesSuccessfulWarningUnlessOutputExists()
    {
        var item = CreateItem();
        item.Status = QueueItemStatus.Warning;
        item.PreProbeResult = new ProbeResult(true, "ok", "ffprobe", FpsOption.FromLabel("30"), CodecName: "h264");

        Assert.True(QueueProcessingEligibilityService.IsProcessable(item, _ => false));
        Assert.False(QueueProcessingEligibilityService.IsProcessable(item, _ => true));
    }

    [Theory]
    [InlineData(QueueItemStatus.Completed)]
    [InlineData(QueueItemStatus.Skipped)]
    [InlineData(QueueItemStatus.Failed)]
    [InlineData(QueueItemStatus.Canceled)]
    [InlineData(QueueItemStatus.Unsupported)]
    [InlineData(QueueItemStatus.Probing)]
    [InlineData(QueueItemStatus.Converting)]
    public void IsProcessable_ExcludesNonRunnableStates(QueueItemStatus status)
    {
        var item = CreateItem();
        item.Status = status;

        Assert.False(QueueProcessingEligibilityService.IsProcessable(item));
    }

    private static QueueItem CreateItem()
    {
        return new QueueItem(
            @"C:\input\clip.dat",
            @"C:\input\clip.mp4",
            OutputDestinationMode.SameFolderAsSource,
            null,
            OutputFormat.Mp4,
            "Remux",
            FpsOption.FromLabel("30"),
            hasExistingDirectOutput: false);
    }
}
