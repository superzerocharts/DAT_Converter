namespace DatConverter.Tests;

public sealed class QueueFpsValidationServiceTests
{
    [Fact]
    public void FindItemsRequiringManualFps_ReturnsEligibleUnresolvedItems()
    {
        var unresolved = CreateItem("needs-fps.dat");
        unresolved.ApplyFpsResolution(QueueItemFpsSettings.AutoDetect(), UnresolvedAutoFps());
        unresolved.Status = QueueItemStatus.Warning;
        unresolved.StatusText = "Needs FPS";

        var completed = CreateItem("completed.dat");
        completed.ApplyFpsResolution(QueueItemFpsSettings.AutoDetect(), UnresolvedAutoFps());
        completed.Status = QueueItemStatus.Completed;

        var manual = CreateItem("manual.dat");
        manual.ApplyFpsResolution(QueueItemFpsSettings.FromManual(FpsOption.FromLabel("30")), QueueItemFpsResolution.FromManual(FpsOption.FromLabel("30")));
        manual.Status = QueueItemStatus.Ready;

        var result = QueueFpsValidationService.FindItemsRequiringManualFps([unresolved, completed, manual]);

        var item = Assert.Single(result);
        Assert.Same(unresolved, item);
    }

    [Fact]
    public void FindItemsRequiringManualFps_IgnoresExistingOutputSkippedItems()
    {
        var existing = CreateItem("existing.dat");
        existing.ApplyFpsResolution(QueueItemFpsSettings.AutoDetect(), UnresolvedAutoFps());
        existing.HasExistingDirectOutput = true;
        QueueItemStatusService.ApplyPostFpsResolutionStatus(existing);

        var result = QueueFpsValidationService.FindItemsRequiringManualFps([existing]);

        Assert.Empty(result);
        Assert.Equal(QueueItemStatus.Skipped, existing.Status);
        Assert.Equal("Exists", existing.StatusText);
    }

    [Fact]
    public void FindItemsRequiringManualFps_BlocksWhenExistingOutputIsChangedToNewPath()
    {
        var item = CreateItem("changed-output.dat");
        item.ApplyFpsResolution(QueueItemFpsSettings.AutoDetect(), UnresolvedAutoFps());
        item.HasExistingDirectOutput = true;
        QueueItemStatusService.ApplyPostFpsResolutionStatus(item);

        item.HasExistingDirectOutput = false;
        QueueItemStatusService.ApplyPostFpsResolutionStatus(item);

        var result = QueueFpsValidationService.FindItemsRequiringManualFps([item]);

        var blocked = Assert.Single(result);
        Assert.Same(item, blocked);
        Assert.Equal(QueueItemStatus.Warning, item.Status);
        Assert.Equal("Needs FPS", item.StatusText);
    }

    [Fact]
    public void BuildManualFpsRequiredMessage_IncludesConciseFileList()
    {
        var first = CreateItem("first.dat");
        first.ApplyFpsResolution(QueueItemFpsSettings.AutoDetect(), UnresolvedAutoFps());
        var second = CreateItem("second.dat");
        second.ApplyFpsResolution(QueueItemFpsSettings.AutoDetect(), UnresolvedAutoFps());

        var message = QueueFpsValidationService.BuildManualFpsRequiredMessage([first, second]);

        Assert.Contains("Some files need a Source FPS before conversion can start.", message);
        Assert.Contains("Auto-detect could not determine the FPS for the files below.", message);
        Assert.Contains("Double-click each row marked \"Needs FPS\" and choose Source FPS.", message);
        Assert.Contains("- first.dat", message);
        Assert.Contains("- second.dat", message);
    }

    [Fact]
    public void MixedQueueBlocksUntilUnresolvedItemGetsManualFps()
    {
        var sidecarAuto = CreateItem("dvrfile00000001.dat");
        sidecarAuto.ApplyFpsResolution(QueueItemFpsSettings.AutoDetect(), AutoFps("Auto 30", "30"));
        sidecarAuto.PreProbeResult = SuccessfulProbe(sidecarAuto.Fps);
        sidecarAuto.Status = QueueItemStatus.Ready;

        var datOnlyAuto = CreateItem("dat_5min_sample.dat");
        datOnlyAuto.ApplyFpsResolution(QueueItemFpsSettings.AutoDetect(), AutoFps("Auto 30", "30"));
        datOnlyAuto.PreProbeResult = SuccessfulProbe(datOnlyAuto.Fps);
        datOnlyAuto.Status = QueueItemStatus.Ready;

        var corrupt = CreateItem("dvrfile00000001_corrupt_fps_timestamps.dat");
        corrupt.ApplyFpsResolution(QueueItemFpsSettings.AutoDetect(), UnresolvedAutoFps());
        corrupt.PreProbeResult = SuccessfulProbe(FpsOption.FromLabel("30"));
        corrupt.Status = QueueItemStatus.Warning;
        corrupt.StatusText = "Needs FPS";

        var blocked = QueueFpsValidationService.FindItemsRequiringManualFps([sidecarAuto, datOnlyAuto, corrupt]);

        var blockedItem = Assert.Single(blocked);
        Assert.Same(corrupt, blockedItem);
        Assert.Equal("Auto 30", sidecarAuto.FpsDisplayLabel);
        Assert.Equal("Auto 30", datOnlyAuto.FpsDisplayLabel);

        corrupt.ApplyFpsResolution(
            QueueItemFpsSettings.FromManual(FpsOption.FromLabel("30")),
            QueueItemFpsResolution.FromManual(FpsOption.FromLabel("30")));
        corrupt.Status = QueueItemStatus.WaitingForProbe;

        Assert.Empty(QueueFpsValidationService.FindItemsRequiringManualFps([sidecarAuto, datOnlyAuto, corrupt]));
        Assert.Equal("30", corrupt.FpsDisplayLabel);
        Assert.Equal("30", corrupt.FfmpegRateValue);
        Assert.Equal("Auto 30", sidecarAuto.FpsDisplayLabel);
        Assert.Equal("Auto 30", datOnlyAuto.FpsDisplayLabel);
    }

    private static QueueItem CreateItem(string fileName)
    {
        var root = Path.Combine(Path.GetTempPath(), "DatConverter.Tests");
        return new QueueItem(
            Path.Combine(root, fileName),
            Path.Combine(root, Path.ChangeExtension(fileName, ".mp4")),
            OutputDestinationMode.SameFolderAsSource,
            null,
            OutputFormat.Mp4,
            "Fast",
            FpsOption.FromLabel("30"),
            hasExistingDirectOutput: false);
    }

    private static QueueItemFpsResolution AutoFps(string label, string ffmpegRate)
    {
        return new QueueItemFpsResolution
        {
            SelectionMode = FpsSelectionMode.AutoDetect,
            DisplayLabel = label,
            FfmpegRateValue = ffmpegRate,
            NominalConversionFps = 30,
            HasResolvedFps = true,
            AutoDetectionSucceeded = true,
            Confidence = "High"
        };
    }

    private static ProbeResult SuccessfulProbe(FpsOption fps)
    {
        return new ProbeResult(true, "ok", "ffprobe", fps, CodecName: "h264", Width: 1920, Height: 1080);
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
