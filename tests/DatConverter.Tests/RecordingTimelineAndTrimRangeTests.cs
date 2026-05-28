namespace DatConverter.Tests;

public sealed class RecordingTimelineAndTrimRangeTests
{
    [Fact]
    public void QueueItem_NoTrim_DefaultsToFullVideo()
    {
        var item = CreateItem();

        Assert.Null(item.TrimRange);
        Assert.Equal("Full video", TrimRangeFormatter.FormatTrimState(item));
    }

    [Fact]
    public void TrimRange_ValidRange_PassesValidation()
    {
        var range = new TrimRange(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30));

        Assert.True(range.TryValidate(TimeSpan.FromMinutes(1), out var message));
        Assert.Null(message);
    }

    [Fact]
    public void TrimRange_EndBeforeStart_FailsValidation()
    {
        var range = new TrimRange(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(10));

        Assert.False(range.TryValidate(TimeSpan.FromMinutes(1), out var message));
        Assert.Equal("End must be after Start.", message);
    }

    [Fact]
    public void TrimRange_BeyondKnownDuration_FailsValidation()
    {
        var range = new TrimRange(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(90));

        Assert.False(range.TryValidate(TimeSpan.FromMinutes(1), out var message));
        Assert.Equal("End must not exceed the known video duration.", message);
    }

    [Fact]
    public void TrimRangeFormatter_WithRecordingTimestamp_CrossesMidnight()
    {
        var timeline = RecordingTimelineBuilder.FromSingleDat(
            @"C:\video\sample.dat",
            TimeSpan.FromMinutes(2),
            new DateTime(2026, 5, 27, 23, 59, 30),
            new DateTime(2026, 5, 28, 0, 1, 30));
        var range = new TrimRange(TimeSpan.FromSeconds(45), TimeSpan.FromSeconds(75));

        var formatted = TrimRangeFormatter.FormatRange(timeline, range);

        Assert.Equal("2026-05-28 00:00:15 \u2192 2026-05-28 00:00:45", formatted);
    }

    [Fact]
    public void TrimRangeFormatter_WithoutRecordingTimestamp_UsesElapsedDisplay()
    {
        var timeline = RecordingTimelineBuilder.FromSingleDat(
            @"C:\video\sample.dat",
            TimeSpan.FromMinutes(2),
            recordingStart: null,
            recordingEnd: null);
        var range = new TrimRange(TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(75));

        var formatted = TrimRangeFormatter.FormatRange(timeline, range);

        Assert.Equal("00:00:15 \u2192 00:01:15", formatted);
    }

    [Fact]
    public void RecordingTimeline_SingleDatWithoutRecordingTimestamp_UsesFrameRecordElapsedDuration()
    {
        using var temp = new TempDirectory();
        var inputPath = Path.Combine(temp.Path, "sample.dat");
        WriteDat(
            inputPath,
            ("H264", 0UL, Payload(0x67, 0x10)),
            ("I264", 3_593_750UL, Payload(0x41, 0x20)));

        var timeline = RecordingTimelineBuilder.FromSingleDat(
            inputPath,
            duration: null,
            recordingStart: null,
            recordingEnd: null);

        Assert.False(timeline.HasRecordingTimestamps);
        Assert.Equal(TimeSpan.FromSeconds(92), timeline.TotalDuration);
        Assert.Equal("00:01:32", TrimRangeFormatter.FormatOffset(timeline, TimeSpan.FromSeconds(92)));
    }

    [Fact]
    public void RecordingTimeline_IgnoresFakeMinimumRecordingTimestamp()
    {
        var timeline = RecordingTimelineBuilder.FromSingleDat(
            @"C:\video\sample.dat",
            TimeSpan.FromMinutes(5),
            DateTime.MinValue,
            DateTime.MinValue.AddMinutes(5));

        Assert.False(timeline.HasRecordingTimestamps);
        Assert.Equal("00:01:32", TrimRangeFormatter.FormatOffset(timeline, TimeSpan.FromSeconds(92)));
    }

    [Fact]
    public void RecordingTimeline_SplitRecording_MapsElapsedOffsetToSegmentAndLocalOffset()
    {
        var plan = new SpotterSplitExportPlan
        {
            ExportFolder = @"C:\video",
            Confidence = "Strong",
            Segments =
            [
                new SpotterSplitExportSegment
                {
                    SegmentNumber = 1,
                    FileName = "dvrfile00000001.dat",
                    FilePath = @"C:\video\dvrfile00000001.dat",
                    StartTime = new DateTime(2026, 5, 27, 10, 0, 0),
                    EndTime = new DateTime(2026, 5, 27, 10, 0, 10)
                },
                new SpotterSplitExportSegment
                {
                    SegmentNumber = 2,
                    FileName = "dvrfile00000002.dat",
                    FilePath = @"C:\video\dvrfile00000002.dat",
                    StartTime = new DateTime(2026, 5, 27, 10, 0, 10),
                    EndTime = new DateTime(2026, 5, 27, 10, 0, 25)
                }
            ]
        };

        var timeline = RecordingTimelineBuilder.FromSplitExportPlan(plan, @"C:\video\dvrfile00000001.dat");

        Assert.True(timeline.TryMapElapsedOffset(TimeSpan.FromSeconds(12), out var position));
        Assert.NotNull(position);
        Assert.Equal(@"C:\video\dvrfile00000002.dat", position!.Segment.SourcePath);
        Assert.Equal(TimeSpan.FromSeconds(2), position.SegmentLocalOffset);
        Assert.Equal(TimeSpan.FromSeconds(25), timeline.TotalDuration);
    }

    [Fact]
    public void RecordingTimeline_SplitRecordingWithoutTimestamps_MapsElapsedOffsetToSegmentAndLocalOffset()
    {
        using var temp = new TempDirectory();
        var firstPath = Path.Combine(temp.Path, "dvrfile00000001.dat");
        var secondPath = Path.Combine(temp.Path, "dvrfile00000002.dat");
        WriteDat(firstPath, ("H264", 0UL, Payload(0x67, 0x10)), ("I264", 390_625UL, Payload(0x41, 0x20)));
        WriteDat(secondPath, ("H264", 0UL, Payload(0x67, 0x30)), ("I264", 781_250UL, Payload(0x41, 0x40)));
        var plan = new SpotterSplitExportPlan
        {
            ExportFolder = temp.Path,
            Confidence = "Strong",
            Segments =
            [
                new SpotterSplitExportSegment
                {
                    SegmentNumber = 1,
                    FileName = Path.GetFileName(firstPath),
                    FilePath = firstPath
                },
                new SpotterSplitExportSegment
                {
                    SegmentNumber = 2,
                    FileName = Path.GetFileName(secondPath),
                    FilePath = secondPath
                }
            ]
        };

        var timeline = RecordingTimelineBuilder.FromSplitExportPlan(plan, firstPath);

        Assert.False(timeline.HasRecordingTimestamps);
        Assert.Equal(TimeSpan.FromSeconds(30), timeline.TotalDuration);
        Assert.True(timeline.TryMapElapsedOffset(TimeSpan.FromSeconds(12), out var position));
        Assert.NotNull(position);
        Assert.Equal(secondPath, position!.Segment.SourcePath);
        Assert.Equal(TimeSpan.FromSeconds(2), position.SegmentLocalOffset);
    }

    [Fact]
    public void QueueItem_ClearTrim_ResetsToFullVideo()
    {
        var item = CreateItem();
        item.TrimRange = new TrimRange(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10));

        item.TrimRange = null;

        Assert.Equal("Full video", TrimRangeFormatter.FormatTrimState(item));
    }

    private static QueueItem CreateItem()
    {
        var root = Path.Combine(Path.GetTempPath(), "DatConverter.Tests");
        return new QueueItem(
            Path.Combine(root, "sample.dat"),
            Path.Combine(root, "sample.mp4"),
            OutputDestinationMode.SameFolderAsSource,
            null,
            OutputFormat.Mp4,
            "Fast",
            FpsOption.FromLabel("30"),
            hasExistingDirectOutput: false);
    }

    private static byte[] Payload(byte nalHeader, byte marker)
    {
        return [0x00, 0x00, 0x01, nalHeader, marker];
    }

    private static void WriteDat(string path, params (string Kind, ulong Timestamp, byte[] Payload)[] records)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        foreach (var record in records)
        {
            using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
            writer.Write(record.Timestamp);
            writer.Write(1920U);
            writer.Write(1080U);
            writer.Write(System.Text.Encoding.ASCII.GetBytes(record.Kind));
            writer.Write((uint)record.Payload.Length);
            writer.Write(record.Payload);
        }
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
