namespace DatConverter.Tests;

public sealed class QueueItemContainerMetadataBuilderTests
{
    [Fact]
    public void Build_FullSplitRecording_UsesRecordingStartCreationTimeAndLogicalTitle()
    {
        var item = CreateSplitItem();

        var metadata = QueueItemContainerMetadataBuilder.Build(item);

        Assert.Equal(new DateTime(2026, 5, 22, 5, 41, 0), metadata.CreationTime);
        Assert.Equal("Cam 8379 - 4 hr clip", metadata.Title);
        Assert.Contains("Recording: 2026-05-22 05:41:00 to 2026-05-22 06:05:00", metadata.Comment);
        Assert.Contains("Source type: Split recording", metadata.Comment);
    }

    [Fact]
    public void Build_TrimmedSplitRecording_UsesTrimStartCreationTime()
    {
        var item = CreateSplitItem();
        item.TrimRange = new TrimRange(TimeSpan.FromMinutes(12), TimeSpan.FromMinutes(15));

        var metadata = QueueItemContainerMetadataBuilder.Build(item);

        Assert.Equal(new DateTime(2026, 5, 22, 5, 53, 0), metadata.CreationTime);
        Assert.Contains("Trim: 2026-05-22 05:53:00 to 2026-05-22 05:56:00", metadata.Comment);
        Assert.Contains("Trim duration: 00:03:00", metadata.Comment);
    }

    [Fact]
    public void Build_RuntimeOnlyTrim_OmitsCreationTimeAndIncludesElapsedDetails()
    {
        var item = CreateSingleItem();
        item.TrimRange = new TrimRange(TimeSpan.FromSeconds(92), TimeSpan.FromSeconds(289));

        var metadata = QueueItemContainerMetadataBuilder.Build(item);

        Assert.Null(metadata.CreationTime);
        Assert.Equal("dvrfile00000001", metadata.Title);
        Assert.Contains("Trim: 00:01:32 to 00:04:49", metadata.Comment);
        Assert.Contains("Trim duration: 00:03:17", metadata.Comment);
    }

    private static QueueItem CreateSingleItem()
    {
        return new QueueItem(
            @"C:\video\dvrfile00000001.dat",
            @"C:\video\dvrfile00000001.mp4",
            OutputDestinationMode.SameFolderAsSource,
            null,
            OutputFormat.Mp4,
            "Fast",
            FpsOption.FromLabel("30"),
            hasExistingDirectOutput: false);
    }

    private static QueueItem CreateSplitItem()
    {
        const string root = @"C:\video\Cam 8379 - 4 hr clip";
        var firstPath = Path.Combine(root, "dvrfile00000001.dat");
        return new QueueItem(
            firstPath,
            Path.Combine(root, "Cam 8379 - 4 hr clip.mp4"),
            OutputDestinationMode.SameFolderAsSource,
            null,
            OutputFormat.Mp4,
            "Fast",
            FpsOption.FromLabel("30"),
            hasExistingDirectOutput: false)
        {
            LogicalOutputBaseName = "Cam 8379 - 4 hr clip",
            SplitExportPlan = new SpotterSplitExportPlan
            {
                ExportFolder = root,
                LogicalOutputBaseName = "Cam 8379 - 4 hr clip",
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
