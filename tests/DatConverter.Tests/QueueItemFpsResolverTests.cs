namespace DatConverter.Tests;

public sealed class QueueItemFpsResolverTests
{
    [Fact]
    public void ResolveQueueItemFps_ManualSelectionUsesManualValue()
    {
        var resolver = CreateResolver(_ => throw new InvalidOperationException("Detector should not run for manual FPS."));

        var result = resolver.ResolveQueueItemFps(@"C:\video\clip.dat", QueueItemFpsSettings.FromManual(FpsOption.FromLabel("25")));

        Assert.Equal(FpsSelectionMode.Manual, result.SelectionMode);
        Assert.Equal("25", result.DisplayLabel);
        Assert.Equal("25", result.FfmpegRateValue);
        Assert.Equal(25, result.NominalConversionFps);
        Assert.Equal("Manual", result.Confidence);
        Assert.Null(result.Warning);
    }

    [Fact]
    public void ResolveQueueItemFps_Manual2997UsesFfmpegRational()
    {
        var resolver = CreateResolver(_ => throw new InvalidOperationException("Detector should not run for manual FPS."));

        var result = resolver.ResolveQueueItemFps(@"C:\video\clip.dat", QueueItemFpsSettings.FromManual(FpsOption.FromLabel("29.97")));

        Assert.Equal("30000/1001", result.FfmpegRateValue);
        Assert.Equal(29.97, result.NominalConversionFps);
    }

    [Fact]
    public void ResolveQueueItemFps_AutoDetectKnown30StoresAuto30()
    {
        var resolver = CreateResolver(_ => new FpsDecisionResult
        {
            AutoDetectionSucceeded = true,
            ShouldUseDetectedRate = true,
            NominalConversionFps = 30,
            FfmpegRateValue = "30",
            UserFacingLabel = "Auto 30 fps",
            Confidence = "High",
            DecisionReason = "Detected from Spotter frame records.",
            TechnicalLogText = "average_fps: 29.900"
        });

        var result = resolver.ResolveQueueItemFps(@"C:\video\clip.dat", QueueItemFpsSettings.AutoDetect());

        Assert.Equal(FpsSelectionMode.AutoDetect, result.SelectionMode);
        Assert.Equal("Auto 30", result.DisplayLabel);
        Assert.Equal("30", result.FfmpegRateValue);
        Assert.Equal(30, result.NominalConversionFps);
        Assert.True(result.AutoDetectionSucceeded);
        Assert.Equal("High", result.Confidence);
        Assert.Null(result.Warning);
        Assert.Equal("Detected from Spotter frame records.", result.DecisionReason);
        Assert.Contains("29.900", result.TechnicalLogText);
    }

    [Fact]
    public void ResolveQueueItemFps_AutoDetectCanProduce25()
    {
        var resolver = CreateResolver(_ => new FpsDecisionResult
        {
            AutoDetectionSucceeded = true,
            ShouldUseDetectedRate = true,
            NominalConversionFps = 25,
            FfmpegRateValue = "25",
            UserFacingLabel = "Auto 25 fps",
            Confidence = "High"
        });

        var result = resolver.ResolveQueueItemFps(@"C:\video\clip.dat", QueueItemFpsSettings.AutoDetect());

        Assert.Equal("Auto 25", result.DisplayLabel);
        Assert.Equal("25", result.FfmpegRateValue);
        Assert.Equal(25, result.NominalConversionFps);
        Assert.Null(result.Warning);
        Assert.Equal("Detected from Spotter frame records.", result.DecisionReason);
    }

    [Fact]
    public void ResolveQueueItemFps_AutoDetectionFailureRequiresManualSelection()
    {
        var resolver = CreateResolver(detection => new FpsDecisionResult
        {
            AutoDetectionSucceeded = false,
            ShouldUseDetectedRate = false,
            NominalConversionFps = 30,
            FfmpegRateValue = "30",
            UserFacingLabel = "30 fps",
            Confidence = "Low",
            DecisionReason = detection.FailureReason,
            TechnicalLogText = "failed technical details"
        }, detectionSucceeded: false);

        var result = resolver.ResolveQueueItemFps(@"C:\video\clip.dat", QueueItemFpsSettings.AutoDetect());

        Assert.Equal("Needs manual selection", result.DisplayLabel);
        Assert.Equal("", result.FfmpegRateValue);
        Assert.Null(result.NominalConversionFps);
        Assert.False(result.HasResolvedFps);
        Assert.True(result.RequiresManualFpsSelection);
        Assert.Equal("Unavailable", result.Confidence);
        Assert.Contains("failed", result.Warning, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("choose Source FPS", result.FpsValidationMessage);
        Assert.Contains("failed technical details", result.TechnicalLogText);
    }

    [Fact]
    public void ResolveQueueItemFps_WithRealCamera205Sample_WhenPresent_ResolvesAuto30()
    {
        var sampleDirectory = @"W:\Projects\Camera 205 Sample\Camera 205 Sample";
        var datPath = Path.Combine(sampleDirectory, "dvrfile00000001.dat");
        if (!File.Exists(datPath))
        {
            return;
        }

        var result = new QueueItemFpsResolver().ResolveQueueItemFps(datPath, QueueItemFpsSettings.AutoDetect());

        Assert.Equal(FpsSelectionMode.AutoDetect, result.SelectionMode);
        Assert.Equal("Auto 30", result.DisplayLabel);
        Assert.Equal("30", result.FfmpegRateValue);
        Assert.Equal(30, result.NominalConversionFps);
        Assert.Equal("High", result.Confidence);
        Assert.Contains("Average FPS: 29.9", result.TechnicalLogText);
    }

    [Fact]
    public void ResolveQueueItemFps_WithRealDatOnlySample_WhenPresent_ResolvesAuto30()
    {
        var datPath = Path.Combine(GetRepositoryRoot(), "test-assets", "samples", "dat_5min_sample.dat");
        if (!File.Exists(datPath))
        {
            return;
        }

        var result = new QueueItemFpsResolver().ResolveQueueItemFps(datPath, QueueItemFpsSettings.AutoDetect());

        Assert.Equal(FpsSelectionMode.AutoDetect, result.SelectionMode);
        Assert.Equal("Auto 30", result.DisplayLabel);
        Assert.Equal("30", result.FfmpegRateValue);
        Assert.Equal(30, result.NominalConversionFps);
        Assert.Equal("Medium", result.Confidence);
    }

    [Fact]
    public void ResolveQueueItemFps_WithRealKnownGoodMultiSegmentExport_WhenPresent_ResolvesAuto30()
    {
        var sampleDirectory = @"W:\Projects\Cam 8379 - 4 hr clip";
        var datPath = Path.Combine(sampleDirectory, "dvrfile00000001.dat");
        if (!File.Exists(datPath))
        {
            return;
        }

        var result = new QueueItemFpsResolver().ResolveQueueItemFps(datPath, QueueItemFpsSettings.AutoDetect());

        Assert.Equal(FpsSelectionMode.AutoDetect, result.SelectionMode);
        Assert.Equal("Auto 30", result.DisplayLabel);
        Assert.Equal("30", result.FfmpegRateValue);
        Assert.Equal(30, result.NominalConversionFps);
        Assert.False(result.RequiresManualFpsSelection);
        Assert.Equal("Medium", result.Confidence);
        Assert.Contains("DefaultTimebase", result.TechnicalLogText);
    }

    [Fact]
    public void ResolveQueueItemFps_WithRealCorruptTimestampSample_WhenPresent_RequiresManualFps()
    {
        var datPath = @"W:\Projects\Camera 205 Sample\Camera 205 Sample\dvrfile00000001_corrupt_fps_timestamps.dat";
        if (!File.Exists(datPath))
        {
            return;
        }

        var result = new QueueItemFpsResolver().ResolveQueueItemFps(datPath, QueueItemFpsSettings.AutoDetect());

        Assert.Equal(FpsSelectionMode.AutoDetect, result.SelectionMode);
        Assert.False(result.HasResolvedFps);
        Assert.True(result.RequiresManualFpsSelection);
        Assert.Equal("Needs manual selection", result.DisplayLabel);
        Assert.Equal("", result.FfmpegRateValue);
        Assert.Equal("Unavailable", result.Confidence);
        Assert.Contains("Unable to calculate FPS", result.Warning);
    }

    private static string GetRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DatConverter.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory.FullName;
    }

    private static QueueItemFpsResolver CreateResolver(
        Func<SpotterFpsDetectionResult, FpsDecisionResult> decide,
        bool detectionSucceeded = true)
    {
        return new QueueItemFpsResolver(
            (_, _) => new SpotterFpsDetectionResult
            {
                Succeeded = detectionSucceeded,
                FailureReason = detectionSucceeded ? "" : "No Spotter frame records were found.",
                Confidence = detectionSucceeded ? "High" : "Low"
            },
            decide,
            _ => null);
    }
}
