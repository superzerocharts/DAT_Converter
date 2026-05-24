namespace DatConverter.Tests;

public sealed class QueueRunningValidationServiceTests
{
    [Fact]
    public void ShouldProbeBeforeContinuing_IncludesUnresolvedNeedsFpsBeforeProbe()
    {
        var item = CreateItem();
        item.ApplyFpsResolution(QueueItemFpsSettings.AutoDetect(), UnresolvedAutoFps());
        item.Status = QueueItemStatus.Warning;
        item.StatusText = "Needs FPS";

        Assert.True(QueueRunningValidationService.ShouldProbeBeforeContinuing(item));
    }

    [Fact]
    public void ShouldProbeBeforeContinuing_ExcludesUnresolvedNeedsFpsAfterProbe()
    {
        var item = CreateItem();
        item.ApplyFpsResolution(QueueItemFpsSettings.AutoDetect(), UnresolvedAutoFps());
        item.Status = QueueItemStatus.Warning;
        item.StatusText = "Needs FPS";
        item.PreProbeResult = new ProbeResult(true, "ok", "ffprobe", FpsOption.FromLabel("30"), CodecName: "h264");

        Assert.False(QueueRunningValidationService.ShouldProbeBeforeContinuing(item));
    }

    [Fact]
    public void ShouldProbeBeforeContinuing_ExcludesResolvedWaitingForProbeBecauseRunnerCanProcessIt()
    {
        var item = CreateItem();
        item.Status = QueueItemStatus.WaitingForProbe;

        Assert.False(QueueRunningValidationService.ShouldProbeBeforeContinuing(item));
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

    private static QueueItemFpsResolution UnresolvedAutoFps()
    {
        return new QueueItemFpsResolution
        {
            SelectionMode = FpsSelectionMode.AutoDetect,
            DisplayLabel = "Needs manual selection",
            FfmpegRateValue = "",
            HasResolvedFps = false,
            RequiresManualFpsSelection = true,
            Confidence = "Unavailable"
        };
    }
}
