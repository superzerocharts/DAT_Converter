namespace DatConverter.Tests;

public sealed class QueuePreProbeServiceTests
{
    [Fact]
    public void ShouldPreProbe_IncludesWaitingForProbeItems()
    {
        var item = CreateItem();

        Assert.True(QueuePreProbeService.ShouldPreProbe(item));
    }

    [Fact]
    public void ShouldPreProbe_IncludesUnresolvedFpsWarningBeforeProbe()
    {
        var item = CreateItem();
        item.ApplyFpsResolution(QueueItemFpsSettings.AutoDetect(), UnresolvedAutoFps());
        item.Status = QueueItemStatus.Warning;
        item.StatusText = "Needs FPS";

        Assert.True(QueuePreProbeService.ShouldPreProbe(item));
    }

    [Fact]
    public void ShouldPreProbe_ExcludesUnresolvedFpsWarningAfterProbe()
    {
        var item = CreateItem();
        item.ApplyFpsResolution(QueueItemFpsSettings.AutoDetect(), UnresolvedAutoFps());
        item.Status = QueueItemStatus.Warning;
        item.StatusText = "Needs FPS";
        item.PreProbeResult = new ProbeResult(true, "ok", "ffprobe", FpsOption.FromLabel("30"), CodecName: "h264");

        Assert.False(QueuePreProbeService.ShouldPreProbe(item));
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
            NominalConversionFps = null,
            HasResolvedFps = false,
            RequiresManualFpsSelection = true,
            FpsValidationMessage = "Auto-detect could not determine the source FPS. Double-click this row and choose Source FPS.",
            Confidence = "Unavailable"
        };
    }
}
