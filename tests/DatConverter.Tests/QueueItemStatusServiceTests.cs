namespace DatConverter.Tests;

public sealed class QueueItemStatusServiceTests
{
    [Fact]
    public void HasReusableProbeForCurrentFps_ReusesProbeOnly30AfterManual30Selection()
    {
        var item = CreateItem();
        item.ApplyFpsResolution(QueueItemFpsSettings.AutoDetect(), new QueueItemFpsResolution
        {
            SelectionMode = FpsSelectionMode.AutoDetect,
            DisplayLabel = "Needs manual selection",
            FfmpegRateValue = "",
            NominalConversionFps = null,
            HasResolvedFps = false,
            RequiresManualFpsSelection = true,
            Confidence = "Unavailable"
        });
        item.PreProbeResult = new ProbeResult(true, "ok", "ffprobe", FpsOption.FromLabel("30"), CodecName: "h264");

        item.ApplyFpsResolution(
            QueueItemFpsSettings.FromManual(FpsOption.FromLabel("30")),
            QueueItemFpsResolution.FromManual(FpsOption.FromLabel("30")));

        Assert.True(QueueItemStatusService.HasReusableProbeForCurrentFps(item));
    }

    [Fact]
    public void HasReusableProbeForCurrentFps_DoesNotReuseProbeWhenManualFpsDiffers()
    {
        var item = CreateItem();
        item.PreProbeResult = new ProbeResult(true, "ok", "ffprobe", FpsOption.FromLabel("30"), CodecName: "h264");
        item.ApplyFpsResolution(
            QueueItemFpsSettings.FromManual(FpsOption.FromLabel("25")),
            QueueItemFpsResolution.FromManual(FpsOption.FromLabel("25")));

        Assert.False(QueueItemStatusService.HasReusableProbeForCurrentFps(item));
    }

    [Fact]
    public void ApplyPostFpsResolutionStatus_PrioritizesExistingOutputOverUnresolvedFps()
    {
        var item = CreateItem(hasExistingDirectOutput: true);
        item.ApplyFpsResolution(QueueItemFpsSettings.AutoDetect(), UnresolvedAutoFps());

        QueueItemStatusService.ApplyPostFpsResolutionStatus(item);

        Assert.Equal(QueueItemStatus.Skipped, item.Status);
        Assert.Equal("Exists", item.StatusText);
        Assert.Equal("Selected output exists", item.ProgressText);
        Assert.True(item.RequiresManualFpsSelection);
    }

    [Fact]
    public void ApplyPostFpsResolutionStatus_ShowsNeedsFpsWhenExistingOutputIsCleared()
    {
        var item = CreateItem(hasExistingDirectOutput: true);
        item.ApplyFpsResolution(QueueItemFpsSettings.AutoDetect(), UnresolvedAutoFps());
        QueueItemStatusService.ApplyPostFpsResolutionStatus(item);

        item.HasExistingDirectOutput = false;
        QueueItemStatusService.ApplyPostFpsResolutionStatus(item);

        Assert.Equal(QueueItemStatus.Warning, item.Status);
        Assert.Equal("Needs FPS", item.StatusText);
        Assert.Equal("Choose Source FPS", item.ProgressText);
    }

    [Fact]
    public void ApplyPreProbeResult_PrioritizesExistingOutputOverUnresolvedFpsWhenProbeSucceeds()
    {
        var item = CreateItem(hasExistingDirectOutput: true);
        item.ApplyFpsResolution(QueueItemFpsSettings.AutoDetect(), UnresolvedAutoFps());

        QueueItemStatusService.ApplyPreProbeResult(item, new ProbeResult(true, "ok", "ffprobe", FpsOption.FromLabel("30"), CodecName: "h264"));

        Assert.Equal(QueueItemStatus.Skipped, item.Status);
        Assert.Equal("Exists", item.StatusText);
        Assert.Equal("Selected output exists", item.ProgressText);
    }

    private static QueueItem CreateItem(bool hasExistingDirectOutput = false)
    {
        return new QueueItem(
            @"C:\input\clip.dat",
            @"C:\input\clip.mp4",
            OutputDestinationMode.SameFolderAsSource,
            null,
            OutputFormat.Mp4,
            "Remux",
            FpsOption.FromLabel("30"),
            hasExistingDirectOutput);
    }

    private static QueueItemFpsResolution UnresolvedAutoFps()
    {
        return new QueueItemFpsResolution
        {
            SelectionMode = FpsSelectionMode.AutoDetect,
            DisplayLabel = "Needs manual selection",
            FfmpegRateValue = "",
            NominalConversionFps = null,
            HasResolvedFps = false,
            RequiresManualFpsSelection = true,
            Confidence = "Unavailable"
        };
    }
}
