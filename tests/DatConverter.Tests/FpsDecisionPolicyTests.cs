namespace DatConverter.Tests;

public sealed class FpsDecisionPolicyTests
{
    [Fact]
    public void Decide_WithKnownSampleShapeAndSidecar_SelectsAuto30WithHighConfidence()
    {
        using var temp = new TempDirectory();
        var datPath = Path.Combine(temp.Path, "sample.dat");
        var sefPath = Path.Combine(temp.Path, "sample.sef2");
        SpotterFpsDetectorTests.WriteDat(datPath, SpotterFpsDetectorTests.BuildSampleShapeTimestamps());
        SpotterFpsDetectorTests.WriteSidecar(sefPath, 298.197);
        var detection = new SpotterFpsDetector().Detect(datPath, sefPath);

        var decision = new FpsDecisionPolicy().Decide(detection);

        Assert.True(decision.AutoDetectionSucceeded);
        Assert.True(decision.ShouldUseDetectedRate);
        Assert.InRange(decision.RawAverageFps!.Value, 29.89, 29.91);
        Assert.InRange(decision.RawBucketMedianFps!.Value, 29.5, 30.5);
        Assert.Equal(30, decision.NominalConversionFps);
        Assert.Equal("30", decision.FfmpegRateValue);
        Assert.Equal("High", decision.Confidence);
        Assert.Equal("Auto 30 fps", decision.UserFacingLabel);
        Assert.Contains("Average FPS: 29.9", decision.TechnicalLogText);
        Assert.Contains("Per-second FPS", decision.TechnicalLogText);
    }

    [Fact]
    public void Decide_WithKnownSampleShapeWithoutSidecar_Selects30WithMediumConfidence()
    {
        using var temp = new TempDirectory();
        var datPath = Path.Combine(temp.Path, "sample.dat");
        SpotterFpsDetectorTests.WriteDat(datPath, SpotterFpsDetectorTests.BuildSampleShapeTimestamps());
        var detection = new SpotterFpsDetector().Detect(datPath);

        var decision = new FpsDecisionPolicy().Decide(detection);

        Assert.True(decision.AutoDetectionSucceeded);
        Assert.True(decision.ShouldUseDetectedRate);
        Assert.Equal(30, decision.NominalConversionFps);
        Assert.Equal("30", decision.FfmpegRateValue);
        Assert.Equal("Medium", decision.Confidence);
    }

    [Fact]
    public void Decide_DoesNotChoose2997ForKnownSampleShape()
    {
        using var temp = new TempDirectory();
        var datPath = Path.Combine(temp.Path, "sample.dat");
        var sefPath = Path.Combine(temp.Path, "sample.sef2");
        SpotterFpsDetectorTests.WriteDat(datPath, SpotterFpsDetectorTests.BuildSampleShapeTimestamps());
        SpotterFpsDetectorTests.WriteSidecar(sefPath, 298.197);
        var detection = new SpotterFpsDetector().Detect(datPath, sefPath);

        var decision = new FpsDecisionPolicy().Decide(detection);

        Assert.NotEqual(29.97, decision.NominalConversionFps);
        Assert.NotEqual("30000/1001", decision.FfmpegRateValue);
    }

    [Fact]
    public void Decide_WithClearSidecarCalibratedNtscEvidence_Selects2997()
    {
        var detection = new SpotterFpsDetectionResult
        {
            Succeeded = true,
            DetectionSource = "DatFrameRecordsWithSefDuration",
            Confidence = "High",
            TechnicalDetails = new SpotterFpsTechnicalDetails
            {
                FrameCount = 8991,
                BucketCount = 300,
                AverageFps = 29.97,
                BucketMedianFps = 30,
                BucketModeFps = 30,
                BucketMinFps = 29,
                BucketMaxFps = 31,
                StableBucketCounts = Enumerable.Repeat(30, 300).ToArray()
            }
        };

        var decision = new FpsDecisionPolicy().Decide(detection);

        Assert.True(decision.ShouldUseDetectedRate);
        Assert.Equal(29.97, decision.NominalConversionFps);
        Assert.Equal("30000/1001", decision.FfmpegRateValue);
        Assert.Equal("High", decision.Confidence);
    }

    [Fact]
    public void Decide_WithDetectionFailure_UsesDefaultFallback()
    {
        var detection = new SpotterFpsDetectionResult
        {
            Succeeded = false,
            FailureReason = "Fewer than two valid records."
        };

        var decision = new FpsDecisionPolicy().Decide(detection);

        Assert.False(decision.AutoDetectionSucceeded);
        Assert.False(decision.ShouldUseDetectedRate);
        Assert.Equal(30, decision.NominalConversionFps);
        Assert.Equal("30", decision.FfmpegRateValue);
        Assert.Equal("Low", decision.Confidence);
        Assert.Equal("30 fps", decision.UserFacingLabel);
    }

    [Fact]
    public void Decide_WithHeavilyConflictingEvidence_ReturnsLowConfidenceFallback()
    {
        var detection = new SpotterFpsDetectionResult
        {
            Succeeded = true,
            DetectionSource = "DatFrameRecordsWithSefDuration",
            Confidence = "High",
            TechnicalDetails = new SpotterFpsTechnicalDetails
            {
                FrameCount = 900,
                BucketCount = 30,
                AverageFps = 20,
                BucketMedianFps = 30,
                BucketModeFps = 30,
                BucketMinFps = 29,
                BucketMaxFps = 31,
                StableBucketCounts = Enumerable.Repeat(30, 30).ToArray()
            }
        };

        var decision = new FpsDecisionPolicy().Decide(detection);

        Assert.True(decision.AutoDetectionSucceeded);
        Assert.False(decision.ShouldUseDetectedRate);
        Assert.Equal("Low", decision.Confidence);
        Assert.Equal("30", decision.FfmpegRateValue);
        Assert.Contains(decision.Warnings, warning => warning.Contains("disagree", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Decide_WithUnstableBuckets_ReturnsLowConfidenceFallback()
    {
        var detection = new SpotterFpsDetectionResult
        {
            Succeeded = true,
            DetectionSource = "DatFrameRecordsWithSefDuration",
            Confidence = "High",
            TechnicalDetails = new SpotterFpsTechnicalDetails
            {
                FrameCount = 900,
                BucketCount = 30,
                AverageFps = 30,
                BucketMedianFps = 30,
                BucketModeFps = 30,
                BucketMinFps = 20,
                BucketMaxFps = 40,
                StableBucketCounts = new[] { 20, 40, 30, 30, 29, 31 }
            }
        };

        var decision = new FpsDecisionPolicy().Decide(detection);

        Assert.False(decision.ShouldUseDetectedRate);
        Assert.Equal("Low", decision.Confidence);
        Assert.Contains(decision.Warnings, warning => warning.Contains("variable", StringComparison.OrdinalIgnoreCase));
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
