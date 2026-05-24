namespace DatConverter.Tests;

public sealed class MirasysFpsDetectorTests
{
    [Fact]
    public void Detect_WithValidSidecar_CalibratesKnownSampleShape()
    {
        using var temp = new TempDirectory();
        var datPath = Path.Combine(temp.Path, "sample.dat");
        var sefPath = Path.Combine(temp.Path, "sample.sef2");
        WriteDat(datPath, BuildSampleShapeTimestamps());
        WriteSidecar(sefPath, 298.197);

        var result = new MirasysFpsDetector().Detect(datPath, sefPath);

        Assert.True(result.Succeeded, result.FailureReason);
        Assert.Equal("DatFrameRecordsWithSefDuration", result.DetectionSource);
        Assert.Equal("High", result.Confidence);
        Assert.Equal(8916, result.TechnicalDetails.FrameCount);
        Assert.Equal(298, result.TechnicalDetails.H264KeyframeCount);
        Assert.Equal(8618, result.TechnicalDetails.I264InterframeCount);
        Assert.Equal(1920, result.TechnicalDetails.Width);
        Assert.Equal(1080, result.TechnicalDetails.Height);
        Assert.InRange(result.TechnicalDetails.TimebaseUnitsPerSecond!.Value, 39062.49, 39062.51);
        Assert.InRange(result.TechnicalDetails.AverageFps!.Value, 29.89, 29.91);
        Assert.Equal(30, result.TechnicalDetails.BucketModeFps);
        Assert.InRange(result.TechnicalDetails.BucketMedianFps!.Value, 29.5, 30.5);
        Assert.Contains("Average FPS: 29.9", result.BuildTechnicalLogText());
        Assert.Contains("Per-second FPS", result.BuildTechnicalLogText());
    }

    [Fact]
    public void Detect_WithoutSidecar_UsesDefaultTimebaseWithMediumConfidence()
    {
        using var temp = new TempDirectory();
        var datPath = Path.Combine(temp.Path, "sample.dat");
        WriteDat(datPath, BuildSampleShapeTimestamps());

        var result = new MirasysFpsDetector().Detect(datPath);

        Assert.True(result.Succeeded, result.FailureReason);
        Assert.Equal("DatFrameRecordsDefaultTimebase", result.DetectionSource);
        Assert.Equal("Medium", result.Confidence);
        Assert.Equal(MirasysFpsDetector.DefaultTimebaseUnitsPerSecond, result.TechnicalDetails.TimebaseUnitsPerSecond);
        Assert.InRange(result.TechnicalDetails.AverageFps!.Value, 29.89, 29.91);
        Assert.Contains(result.TechnicalDetails.Warnings, warning => warning.Contains("No .sef/.sef2", StringComparison.Ordinal));
    }

    [Fact]
    public void Detect_WithInvalidSidecar_FallsBackToDefaultTimebase()
    {
        using var temp = new TempDirectory();
        var datPath = Path.Combine(temp.Path, "sample.dat");
        var sefPath = Path.Combine(temp.Path, "bad.sef2");
        WriteDat(datPath, BuildConstantRateTimestamps(frameCount: 120, fps: 30, seconds: 4));
        File.WriteAllText(sefPath, "<archive2><start>bad</start><end>also bad</end></archive2>");

        var result = new MirasysFpsDetector().Detect(datPath, sefPath);

        Assert.True(result.Succeeded, result.FailureReason);
        Assert.Equal("DatFrameRecordsDefaultTimebase", result.DetectionSource);
        Assert.Equal("Medium", result.Confidence);
        Assert.Contains(result.TechnicalDetails.Warnings, warning => warning.Contains("sidecar", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Detect_WithTooFewRecords_FailsCleanly()
    {
        using var temp = new TempDirectory();
        var datPath = Path.Combine(temp.Path, "short.dat");
        WriteDat(datPath, new[] { 1000UL });

        var result = new MirasysFpsDetector().Detect(datPath);

        Assert.False(result.Succeeded);
        Assert.Equal("Low", result.Confidence);
        Assert.Contains("Fewer than two", result.FailureReason);
    }

    [Fact]
    public void Detect_WithInvalidDat_FailsCleanly()
    {
        using var temp = new TempDirectory();
        var datPath = Path.Combine(temp.Path, "invalid.dat");
        File.WriteAllText(datPath, "not a mirasys frame record file");

        var result = new MirasysFpsDetector().Detect(datPath);

        Assert.False(result.Succeeded);
        Assert.Equal(0, result.TechnicalDetails.FrameCount);
        Assert.Contains("Fewer than two", result.FailureReason);
    }

    [Fact]
    public void Detect_WithZeroTimestampSpan_FailsCleanly()
    {
        using var temp = new TempDirectory();
        var datPath = Path.Combine(temp.Path, "zero-timestamps.dat");
        WriteDat(datPath, Enumerable.Repeat(0UL, 120).ToArray());

        var result = new MirasysFpsDetector().Detect(datPath);

        Assert.False(result.Succeeded);
        Assert.Equal("Unable to calculate FPS from Mirasys timestamps.", result.FailureReason);
        Assert.Equal(120, result.TechnicalDetails.FrameCount);
        Assert.Contains("Unable to calculate FPS", result.BuildTechnicalLogText());
    }

    [Fact]
    public void Detect_WithRealCamera205Sample_WhenPresent_MatchesAuditEvidence()
    {
        var sampleDirectory = @"W:\Projects\Camera 205 Sample\Camera 205 Sample";
        var datPath = Path.Combine(sampleDirectory, "dvrfile00000001.dat");
        var sefPath = Path.Combine(sampleDirectory, "Camera 205 Sample.sef2");
        if (!File.Exists(datPath) || !File.Exists(sefPath))
        {
            return;
        }

        var result = new MirasysFpsDetector().Detect(datPath, sefPath);

        Assert.True(result.Succeeded, result.FailureReason);
        Assert.Equal(8916, result.TechnicalDetails.FrameCount);
        Assert.Equal(298, result.TechnicalDetails.H264KeyframeCount);
        Assert.Equal(8618, result.TechnicalDetails.I264InterframeCount);
        Assert.InRange(result.TechnicalDetails.AverageFps!.Value, 29.89, 29.91);
        Assert.Equal(30, result.TechnicalDetails.BucketModeFps);
    }

    [Fact]
    public void Detect_WithRealDatOnlySample_WhenPresent_UsesDefaultTimebase()
    {
        var samplePath = Path.Combine(GetRepositoryRoot(), "test-assets", "samples", "dat_5min_sample.dat");
        if (!File.Exists(samplePath))
        {
            return;
        }

        var result = new MirasysFpsDetector().Detect(samplePath);

        Assert.True(result.Succeeded, result.FailureReason);
        Assert.Equal("DatFrameRecordsDefaultTimebase", result.DetectionSource);
        Assert.Equal("Medium", result.Confidence);
        Assert.Equal(30, result.TechnicalDetails.BucketModeFps);
    }

    [Fact]
    public void Detect_WithRealCorruptTimestampSample_WhenPresent_FailsWithoutThrowing()
    {
        var sampleDirectory = @"W:\Projects\Camera 205 Sample\Camera 205 Sample";
        var datPath = Path.Combine(sampleDirectory, "dvrfile00000001_corrupt_fps_timestamps.dat");
        if (!File.Exists(datPath))
        {
            return;
        }

        var result = new MirasysFpsDetector().Detect(datPath);

        Assert.False(result.Succeeded);
        Assert.Equal("Unable to calculate FPS from Mirasys timestamps.", result.FailureReason);
        Assert.Equal(8916, result.TechnicalDetails.FrameCount);
    }

    internal static ulong[] BuildSampleShapeTimestamps()
    {
        const int frameCount = 8916;
        const double durationSeconds = 298.197;
        const double timebase = 39062.502306;
        var span = (ulong)Math.Round(durationSeconds * timebase);
        var timestamps = new ulong[frameCount];
        const ulong firstTimestamp = 74554267784667818;

        for (var index = 0; index < timestamps.Length; index++)
        {
            timestamps[index] = firstTimestamp + (ulong)Math.Round(index * (span / (double)(frameCount - 1)));
        }

        return timestamps;
    }

    internal static ulong[] BuildConstantRateTimestamps(int frameCount, double fps, double seconds)
    {
        var span = (ulong)Math.Round(seconds * MirasysFpsDetector.DefaultTimebaseUnitsPerSecond);
        var timestamps = new ulong[frameCount];
        const ulong firstTimestamp = 1000000;

        for (var index = 0; index < timestamps.Length; index++)
        {
            timestamps[index] = firstTimestamp + (ulong)Math.Round(index * (span / (double)(frameCount - 1)));
        }

        _ = fps;
        return timestamps;
    }

    internal static void WriteDat(string path, IReadOnlyList<ulong> timestamps, IReadOnlySet<int>? lowPayloadIndexes = null)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var writer = new BinaryWriter(stream);

        for (var index = 0; index < timestamps.Count; index++)
        {
            writer.Write(timestamps[index]);
            writer.Write(1920U);
            writer.Write(1080U);
            writer.Write(index % 30 == 0 ? new byte[] { (byte)'H', (byte)'2', (byte)'6', (byte)'4' } : new byte[] { (byte)'I', (byte)'2', (byte)'6', (byte)'4' });
            writer.Write(1U);
            writer.Write((byte)(lowPayloadIndexes?.Contains(index) == true ? 0 : 1));
        }
    }

    internal static void WriteSidecar(string path, double durationSeconds)
    {
        var start = new DateTime(2026, 5, 20, 4, 25, 16, DateTimeKind.Unspecified).AddMilliseconds(541);
        var end = start.AddSeconds(durationSeconds);
        File.WriteAllText(
            path,
            $"""
            <archive2>
              <start>{start:O}</start>
              <end>{end:O}</end>
            </archive2>
            """);
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
