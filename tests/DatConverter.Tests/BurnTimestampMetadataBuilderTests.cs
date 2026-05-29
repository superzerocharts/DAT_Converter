namespace DatConverter.Tests;

public sealed class BurnTimestampMetadataBuilderTests
{
    [Fact]
    public void Build_WithTrimUsesTrimStartTime()
    {
        var item = CreateSplitItem();
        item.BurnTimestamp = true;
        item.TrimRange = new TrimRange(TimeSpan.FromMinutes(12), TimeSpan.FromMinutes(15));
        var timeline = RecordingTimelineBuilder.Build(item);

        var options = BurnTimestampMetadataBuilder.Build(item, timeline);

        Assert.NotNull(options);
        Assert.Equal("Cam 8379 - 4 hr clip", options.CameraName);
        Assert.Equal(new DateTime(2026, 5, 22, 5, 53, 0), options.StartTime);
    }

    [Fact]
    public void Build_WithSplitCameraDisplayName_PrefersCameraDisplayName()
    {
        var item = CreateSplitItem("8379 Marquee Northeast PTZ");
        item.BurnTimestamp = true;
        var timeline = RecordingTimelineBuilder.Build(item);

        var options = BurnTimestampMetadataBuilder.Build(item, timeline);

        Assert.NotNull(options);
        Assert.Equal("8379 Marquee Northeast PTZ", options.CameraName);
    }

    [Fact]
    public void Build_RuntimeOnlyTimelineReturnsNull()
    {
        var item = new QueueItem(
            @"C:\video\dvrfile00000001.dat",
            @"C:\video\dvrfile00000001.mp4",
            OutputDestinationMode.SameFolderAsSource,
            null,
            OutputFormat.Mp4,
            "Encode",
            FpsOption.FromLabel("30"),
            hasExistingDirectOutput: false)
        {
            BurnTimestamp = true
        };
        var timeline = RecordingTimelineBuilder.Build(item);

        Assert.Null(BurnTimestampMetadataBuilder.Build(item, timeline));
    }

    [Theory]
    [InlineData("Encode", true)]
    [InlineData("EncodeNvenc", true)]
    [InlineData("Full NVENC", true)]
    [InlineData("Fast", false)]
    [InlineData("Remux", false)]
    public void IsSupportedMode_RequiresEncode(string mode, bool expected)
    {
        Assert.Equal(expected, BurnTimestampMetadataBuilder.IsSupportedMode(mode));
    }

    private static QueueItem CreateSplitItem(string? cameraDisplayName = null)
    {
        const string root = @"C:\video\Cam 8379 - 4 hr clip";
        var firstPath = Path.Combine(root, "dvrfile00000001.dat");
        return new QueueItem(
            firstPath,
            Path.Combine(root, "Cam 8379 - 4 hr clip.mp4"),
            OutputDestinationMode.SameFolderAsSource,
            null,
            OutputFormat.Mp4,
            "Encode",
            FpsOption.FromLabel("30"),
            hasExistingDirectOutput: false)
        {
            LogicalOutputBaseName = "Cam 8379 - 4 hr clip",
            SplitExportPlan = new SpotterSplitExportPlan
            {
                ExportFolder = root,
                LogicalOutputBaseName = "Cam 8379 - 4 hr clip",
                CameraDisplayName = cameraDisplayName,
                Confidence = "Strong",
                Segments =
                [
                    new SpotterSplitExportSegment
                    {
                        SegmentNumber = 1,
                        FileName = "dvrfile00000001.dat",
                        FilePath = firstPath,
                        StartTime = new DateTime(2026, 5, 22, 5, 41, 0),
                        EndTime = new DateTime(2026, 5, 22, 5, 53, 0)
                    },
                    new SpotterSplitExportSegment
                    {
                        SegmentNumber = 2,
                        FileName = "dvrfile00000002.dat",
                        FilePath = Path.Combine(root, "dvrfile00000002.dat"),
                        StartTime = new DateTime(2026, 5, 22, 5, 53, 0),
                        EndTime = new DateTime(2026, 5, 22, 6, 5, 0)
                    }
                ]
            }
        };
    }
}
