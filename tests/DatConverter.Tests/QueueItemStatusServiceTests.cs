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
    public void ApplyCombinedStoryboardReadiness_MarksValidResolvedStoryboardReadyWithoutProbe()
    {
        using var temp = new TempDirectory();
        var item = CreateCombinedStoryboardItem(temp.Path);

        Assert.True(QueueItemStatusService.ApplyCombinedStoryboardReadiness(item));

        Assert.Null(item.PreProbeResult);
        Assert.Equal(QueueItemStatus.Ready, item.Status);
        Assert.Equal("Ready", item.StatusText);
        Assert.Equal("Ready", item.ProgressText);
    }

    [Fact]
    public void ApplyCombinedStoryboardReadiness_MarksMissingClipUnsupported()
    {
        using var temp = new TempDirectory();
        var item = CreateCombinedStoryboardItem(temp.Path, createClipFile: false);

        QueueItemStatusService.ApplyCombinedStoryboardReadiness(item);

        Assert.Equal(QueueItemStatus.Unsupported, item.Status);
        Assert.Equal("Unsupported", item.StatusText);
        Assert.Equal("Missing clip 1", item.ProgressText);
    }

    [Fact]
    public void ApplyCombinedStoryboardReadiness_PrioritizesExistingOutput()
    {
        using var temp = new TempDirectory();
        var item = CreateCombinedStoryboardItem(temp.Path, hasExistingDirectOutput: true);

        QueueItemStatusService.ApplyCombinedStoryboardReadiness(item);

        Assert.Equal(QueueItemStatus.Skipped, item.Status);
        Assert.Equal("Exists", item.StatusText);
        Assert.Equal("Selected output exists", item.ProgressText);
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

    private static QueueItem CreateCombinedStoryboardItem(string folder, bool createClipFile = true, bool hasExistingDirectOutput = false)
    {
        var sidecarPath = Path.Combine(folder, "Storyboard.sef2");
        File.WriteAllText(sidecarPath, "<archive2 />");
        var clipPath = Path.Combine(folder, "clip1.dat");
        if (createClipFile)
        {
            File.WriteAllText(clipPath, "payload");
        }

        return new QueueItem(
            sidecarPath,
            Path.Combine(folder, "Storyboard.mp4"),
            OutputDestinationMode.SameFolderAsSource,
            null,
            OutputFormat.Mp4,
            "Encode",
            FpsOption.FromLabel("30"),
            hasExistingDirectOutput)
        {
            IsCombinedStoryboard = true,
            StoryboardPlan = new SpotterStoryboardPlan
            {
                ExportFolder = folder,
                SidecarPath = sidecarPath,
                Clips =
                [
                    new SpotterStoryboardClip
                    {
                        ClipNumber = 1,
                        ClipName = "Clip 1",
                        DatFileName = Path.GetFileName(clipPath),
                        DatFilePath = clipPath
                    }
                ]
            },
            PreProbeResult = new ProbeResult(true, "stale", "ffprobe", FpsOption.FromLabel("30"))
        };
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
