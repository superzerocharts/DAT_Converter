namespace DatConverter.Tests;

public sealed class TrimOutputNameFormatterTests
{
    [Fact]
    public void BuildTrimSuffix_WithRecordingDateTime_UsesCompactDateTimeRange()
    {
        var timeline = RecordingTimelineBuilder.FromSingleDat(
            @"C:\video\camera.dat",
            TimeSpan.FromMinutes(20),
            new DateTime(2026, 5, 21, 23, 50, 0),
            new DateTime(2026, 5, 22, 0, 10, 0));
        var range = new TrimRange(TimeSpan.FromMinutes(8), TimeSpan.FromMinutes(20));

        var suffix = TrimOutputNameFormatter.BuildTrimSuffix(timeline, range);

        Assert.Equal("_trim_260521_2358-260522_0010", suffix);
    }

    [Fact]
    public void BuildTrimSuffix_WithRecordingDateTime_CrossingMidnightIncludesBothDates()
    {
        var timeline = RecordingTimelineBuilder.FromSingleDat(
            @"C:\video\camera.dat",
            TimeSpan.FromMinutes(20),
            new DateTime(2026, 5, 21, 23, 58, 0),
            new DateTime(2026, 5, 22, 0, 18, 0));
        var range = new TrimRange(TimeSpan.Zero, TimeSpan.FromMinutes(12));

        var suffix = TrimOutputNameFormatter.BuildTrimSuffix(timeline, range);

        Assert.Equal("_trim_260521_2358-260522_0010", suffix);
    }

    [Fact]
    public void BuildTrimSuffix_WithoutRecordingDateTime_UsesElapsedRuntimeRange()
    {
        var timeline = RecordingTimelineBuilder.FromSingleDat(
            @"C:\video\dvrfile00000001.dat",
            TimeSpan.FromMinutes(10),
            recordingStart: null,
            recordingEnd: null);
        var range = new TrimRange(TimeSpan.FromSeconds(32), TimeSpan.FromSeconds(107));

        var suffix = TrimOutputNameFormatter.BuildTrimSuffix(timeline, range);

        Assert.Equal("_trim_000032-000147", suffix);
    }

    [Fact]
    public void BuildTrimSuffix_WithoutRecordingDateTime_UsesElapsedRuntimeRangeOverOneHour()
    {
        var timeline = RecordingTimelineBuilder.FromSingleDat(
            @"C:\video\dvrfile00000001.dat",
            TimeSpan.FromHours(2),
            recordingStart: null,
            recordingEnd: null);
        var range = new TrimRange(
            new TimeSpan(1, 2, 3),
            new TimeSpan(1, 5, 9));

        var suffix = TrimOutputNameFormatter.BuildTrimSuffix(timeline, range);

        Assert.Equal("_trim_010203-010509", suffix);
    }

    [Fact]
    public void PlanUniqueOutputPath_WithTrimSuffix_UsesExistingDuplicateSuffixPattern()
    {
        using var temp = new TempDirectory();
        var inputPath = Path.Combine(temp.Path, "dvrfile00000001.dat");
        File.WriteAllText(inputPath, "source");
        File.WriteAllText(Path.Combine(temp.Path, "dvrfile00000001_trim_000032-000147.mp4"), "existing");

        var outputPath = OutputPathService.PlanUniqueOutputPath(
            inputPath,
            temp.Path,
            OutputFormat.Mp4,
            _ => true,
            allowExistingDirectOutput: false,
            baseNameSuffix: "_trim_000032-000147");

        Assert.Equal(Path.Combine(temp.Path, "dvrfile00000001_trim_000032-000147_01.mp4"), outputPath);
    }

    [Fact]
    public void GetDirectOutputPath_WithTrimSuffix_UsesTrimName()
    {
        using var temp = new TempDirectory();
        var inputPath = Path.Combine(temp.Path, "dvrfile00000001.dat");
        File.WriteAllText(inputPath, "source");

        var outputPath = OutputPathService.GetDirectOutputPath(
            inputPath,
            temp.Path,
            OutputFormat.Mp4,
            "_trim_000032-000147");

        Assert.Equal(Path.Combine(temp.Path, "dvrfile00000001_trim_000032-000147.mp4"), outputPath);
    }

    [Fact]
    public void GetDirectOutputPath_WhenTrimCleared_ReturnsFullVideoName()
    {
        using var temp = new TempDirectory();
        var inputPath = Path.Combine(temp.Path, "dvrfile00000001.dat");
        File.WriteAllText(inputPath, "source");

        var outputPath = OutputPathService.GetDirectOutputPath(
            inputPath,
            temp.Path,
            OutputFormat.Mp4,
            baseNameSuffix: null);

        Assert.Equal(Path.Combine(temp.Path, "dvrfile00000001.mp4"), outputPath);
    }

    [Fact]
    public void QueueItem_UserCustomOutputPathFlagDistinguishesManualSaveAsAndResetsWithDefaults()
    {
        var item = new QueueItem(
            @"C:\video\sample.dat",
            @"C:\video\sample.mp4",
            OutputDestinationMode.SameFolderAsSource,
            null,
            OutputFormat.Mp4,
            "Fast",
            FpsOption.FromLabel("30"),
            hasExistingDirectOutput: false);

        Assert.False(item.HasUserCustomOutputPath);
        item.HasUserCustomOutputPath = true;

        Assert.True(item.HasUserCustomOutputPath);

        item.ClearCustomSettings();

        Assert.False(item.HasUserCustomOutputPath);
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
