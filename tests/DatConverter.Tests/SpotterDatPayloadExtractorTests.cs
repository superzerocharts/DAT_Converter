namespace DatConverter.Tests;

public sealed class SpotterDatPayloadExtractorTests
{
    [Fact]
    public void Extract_SkipsObviousWrapperBytesBeforeFirstStartCode()
    {
        using var temp = new TempDirectory();
        var datPath = Path.Combine(temp.Path, "sample.dat");
        var outputPath = Path.Combine(temp.Path, "sample.clean.h264");
        var payload = new byte[]
        {
            0xAA, 0xBB, 0xCC,
            0x00, 0x00, 0x01, 0x67, 0x11,
            0x00, 0x00, 0x01, 0x68, 0x22,
            0x00, 0x00, 0x01, 0x65, 0x33
        };
        WriteDat(datPath, payload);

        var result = new SpotterDatPayloadExtractor().Extract(datPath, outputPath);

        Assert.True(result.Succeeded, result.FailureReason);
        Assert.True(result.SkippedWrapperBytes);
        Assert.Equal(3, result.SkippedWrapperByteCount);
        Assert.Equal(1, result.FrameRecordCount);
        Assert.Equal(1, result.ExtractedFrameRecordCount);
        Assert.Equal(3, result.CandidateNalUnitCount);
        Assert.Equal(3, result.StartCodeCount);
        Assert.Equal(0, result.SkippedLeadingByteCount);
        Assert.Equal(new FileInfo(datPath).Length - result.ExtractedPayloadByteCount, result.SkippedNonPayloadByteCount);
        Assert.True(result.LookedConfident);
        Assert.Equal(new byte[]
        {
            0x00, 0x00, 0x01, 0x67, 0x11,
            0x00, 0x00, 0x01, 0x68, 0x22,
            0x00, 0x00, 0x01, 0x65, 0x33
        }, File.ReadAllBytes(outputPath));
    }

    [Fact]
    public void Extract_MultipleRecords_AppendsPayloadsAndAggregatesStats()
    {
        using var temp = new TempDirectory();
        var datPath = Path.Combine(temp.Path, "sample.dat");
        var outputPath = Path.Combine(temp.Path, "sample.clean.h264");
        WriteDat(
            datPath,
            new byte[] { 0x00, 0x00, 0x01, 0x67, 0x11 },
            new byte[] { 0x00, 0x00, 0x01, 0x68, 0x22, 0x00, 0x00, 0x01, 0x65, 0x33 });

        var result = new SpotterDatPayloadExtractor().Extract(datPath, outputPath);

        Assert.True(result.Succeeded, result.FailureReason);
        Assert.Equal(2, result.FrameRecordCount);
        Assert.Equal(2, result.ExtractedFrameRecordCount);
        Assert.Equal(3, result.CandidateNalUnitCount);
        Assert.Equal(1, result.SpsCount);
        Assert.Equal(1, result.PpsCount);
        Assert.Equal(1, result.IdrFrameCount);
        Assert.True(result.ExtractedSourcePercentage > 0);
        Assert.Contains("Extracted/source percentage:", result.BuildTechnicalReport());
        Assert.Contains("Extraction looked confident: True", result.BuildTechnicalReport());
        Assert.Equal(15, new FileInfo(outputPath).Length);
    }

    [Fact]
    public void Extract_RandomDataProducesSafeFailureWithoutOutput()
    {
        using var temp = new TempDirectory();
        var datPath = Path.Combine(temp.Path, "random.dat");
        var outputPath = Path.Combine(temp.Path, "random.clean.h264");
        File.WriteAllBytes(datPath, Enumerable.Range(0, 512).Select(index => (byte)(index % 251)).ToArray());

        var result = new SpotterDatPayloadExtractor().Extract(datPath, outputPath);

        Assert.False(result.Succeeded);
        Assert.Contains("No confident", result.FailureReason);
        Assert.False(result.LookedConfident);
        Assert.Contains(result.Warnings, warning => warning.Contains("must not be used automatically", StringComparison.Ordinal));
        Assert.Contains("Extraction looked confident: False", result.BuildTechnicalReport());
        Assert.False(File.Exists(outputPath));
    }

    [Fact]
    public void Extract_PreCanceled_ThrowsAndDoesNotCreateOutput()
    {
        using var temp = new TempDirectory();
        var datPath = Path.Combine(temp.Path, "sample.dat");
        var outputPath = Path.Combine(temp.Path, "sample.clean.h264");
        WriteDat(datPath, new byte[] { 0x00, 0x00, 0x01, 0x67, 0x11 });
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            new SpotterDatPayloadExtractor().Extract(datPath, outputPath, cts.Token));
        Assert.False(File.Exists(outputPath));
    }

    [Fact]
    public void Extract_RecordAfterLargePadding_UsesStreamingScanAndExtractsPayload()
    {
        using var temp = new TempDirectory();
        var datPath = Path.Combine(temp.Path, "large-padding.dat");
        var outputPath = Path.Combine(temp.Path, "large-padding.clean.h264");
        using (var stream = new FileStream(datPath, FileMode.Create, FileAccess.Write))
        {
            var padding = new byte[2 * 1024 * 1024];
            stream.Write(padding);
            WriteDatRecord(stream, new byte[] { 0x00, 0x00, 0x01, 0x67, 0x11 });
        }

        var result = new SpotterDatPayloadExtractor().Extract(datPath, outputPath);

        Assert.True(result.Succeeded, result.FailureReason);
        Assert.Equal(1, result.FrameRecordCount);
        Assert.Equal(5, result.ExtractedPayloadByteCount);
        Assert.Equal(new byte[] { 0x00, 0x00, 0x01, 0x67, 0x11 }, File.ReadAllBytes(outputPath));
    }

    [Fact]
    public void Extract_WithRealFourHourSegmentOne_WhenPresent_CreatesPrototypeReportOrOutput()
    {
        var samplePath = @"W:\Projects\Cam 8379 - 4 hr clip\dvrfile00000001.dat";
        if (!File.Exists(samplePath))
        {
            return;
        }

        using var temp = new TempDirectory();
        var outputDirectory = temp.Path;
        var outputPath = Path.Combine(outputDirectory, "dvrfile00000001.clean.prototype.h264");

        var result = new SpotterDatPayloadExtractor().Extract(samplePath, outputPath);
        File.WriteAllText(Path.Combine(outputDirectory, "dvrfile00000001.clean.prototype.report.txt"), result.BuildTechnicalReport());

        Assert.True(result.Succeeded || !string.IsNullOrWhiteSpace(result.FailureReason));
        if (result.Succeeded)
        {
            Assert.True(File.Exists(outputPath));
            Assert.True(result.ExtractedPayloadByteCount > 0);
        }
    }

    private static void WriteDat(string path, params byte[][] payloads)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        foreach (var payload in payloads)
        {
            WriteDatRecord(stream, payload);
        }
    }

    private static void WriteDatRecord(Stream stream, byte[] payload)
    {
        using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        writer.Write(123456789UL);
        writer.Write(1920U);
        writer.Write(1080U);
        writer.Write(new byte[] { (byte)'H', (byte)'2', (byte)'6', (byte)'4' });
        writer.Write((uint)payload.Length);
        writer.Write(payload);
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
