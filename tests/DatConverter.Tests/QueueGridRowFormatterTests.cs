namespace DatConverter.Tests;

public sealed class QueueGridRowFormatterTests
{
    [Fact]
    public void ReadyItemDisplaysResolutionInResolutionColumn()
    {
        var item = CreateReadyItem(new ProbeResult(true, "ok", "ffprobe", FpsOption.FromLabel("30"), Width: 1920, Height: 1080));

        Assert.Equal("1920x1080", QueueGridRowFormatter.FormatResolution(item));
    }

    [Fact]
    public void ProgressColumnDoesNotDisplayResolution()
    {
        var item = CreateReadyItem(new ProbeResult(true, "ok", "ffprobe", FpsOption.FromLabel("30"), Width: 1920, Height: 1080));
        item.ProgressText = "1920x1080";

        Assert.Equal(string.Empty, QueueGridRowFormatter.FormatProgress(item));
    }

    [Fact]
    public void ConversionProgressStillDisplaysInProgress()
    {
        var item = CreateReadyItem();
        item.Status = QueueItemStatus.Converting;
        item.ProgressText = "42%";

        Assert.Equal("42%", QueueGridRowFormatter.FormatProgress(item));
    }

    [Fact]
    public void UnknownResolutionDisplaysUnknownAfterSuccessfulProbe()
    {
        var item = CreateReadyItem(new ProbeResult(true, "ok", "ffprobe", FpsOption.FromLabel("30")));

        Assert.Equal("Unknown", QueueGridRowFormatter.FormatResolution(item));
        Assert.Equal(string.Empty, QueueGridRowFormatter.FormatProgress(item));
    }

    private static QueueItem CreateReadyItem(ProbeResult? probeResult = null)
    {
        return new QueueItem(
            @"C:\video\clip.dat",
            @"C:\video\clip.mp4",
            OutputDestinationMode.SameFolderAsSource,
            null,
            OutputFormat.Mp4,
            "Remux",
            FpsOption.FromLabel("30"),
            hasExistingDirectOutput: false)
        {
            Status = QueueItemStatus.Ready,
            StatusText = "Ready",
            ProgressText = string.Empty,
            PreProbeResult = probeResult
        };
    }
}
