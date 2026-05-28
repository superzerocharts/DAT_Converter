namespace DatConverter.Tests;

public sealed class TrimPreviewFrameProviderTests
{
    [Fact]
    public void PreviewProvider_Constructed_DoesNotInvokeFfmpeg()
    {
        var callCount = 0;
        using var provider = new TrimPreviewFrameProvider(
            CreateTools(),
            (_, _, _, _) =>
            {
                callCount++;
                return Task.FromResult(new ProcessRunResult(0, false, false, "", ""));
            });

        Assert.Equal(0, callCount);
        Assert.True(Directory.Exists(provider.TempDirectory));
    }

    [Fact]
    public void TryPlanFrameRequest_UsesBundledFfmpegPath()
    {
        var tools = CreateTools();
        var timeline = RecordingTimelineBuilder.FromSingleDat(
            @"C:\video\sample.dat",
            TimeSpan.FromMinutes(5),
            recordingStart: null,
            recordingEnd: null);

        var planned = TrimPreviewFrameProvider.TryPlanFrameRequest(
            tools,
            timeline,
            TimeSpan.FromSeconds(12.5),
            FpsOption.FromLabel("30"),
            @"C:\temp\window.h264",
            @"C:\temp\preview.jpg",
            TimeSpan.FromSeconds(1.5),
            TimeSpan.FromSeconds(11),
            out var request);

        Assert.True(planned);
        Assert.NotNull(request);
        Assert.Equal(tools.FfmpegPath, request!.FfmpegPath);
        Assert.Equal(@"C:\video\sample.dat", request.SourcePath);
        Assert.Equal(@"C:\temp\window.h264", request.PreviewInputPath);
        Assert.Equal(TimeSpan.FromSeconds(12.5), request.SourceLocalOffset);
        Assert.Equal(TimeSpan.FromSeconds(1.5), request.PreviewSeekOffset);
        Assert.Equal(["-y", "-hide_banner", "-loglevel", "warning", "-fflags", "+genpts+discardcorrupt", "-err_detect", "ignore_err", "-f", "h264", "-r", "30", "-i", @"C:\temp\window.h264", "-ss", "00:00:01.500", "-frames:v", "1", "-q:v", "2", @"C:\temp\preview.jpg"], request.Arguments);
    }

    [Fact]
    public void TryPlanFrameRequest_SplitRecording_MapsTimelinePositionToSegmentLocalOffset()
    {
        var tools = CreateTools();
        var timeline = RecordingTimelineBuilder.FromSplitExportPlan(CreateSplitPlan(), @"C:\video\dvrfile00000001.dat");

        var planned = TrimPreviewFrameProvider.TryPlanFrameRequest(
            tools,
            timeline,
            TimeSpan.FromSeconds(12),
            FpsOption.FromLabel("25"),
            @"C:\temp\window.h264",
            @"C:\temp\preview.jpg",
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(10),
            out var request);

        Assert.True(planned);
        Assert.NotNull(request);
        Assert.Equal(@"C:\video\dvrfile00000002.dat", request!.SourcePath);
        Assert.Equal(TimeSpan.FromSeconds(12), request.TimelineOffset);
        Assert.Equal(TimeSpan.FromSeconds(2), request.SourceLocalOffset);
        Assert.Contains("00:00:02.000", request.Arguments);
    }

    [Fact]
    public void DatPreviewWindowExtractor_ChoosesUsableH264KeyframeWindowBeforeRequestedOffset()
    {
        using var temp = new TempDirectory();
        var datPath = Path.Combine(temp.Path, "sample.dat");
        var outputPath = Path.Combine(temp.Path, "window.h264");
        WriteDat(
            datPath,
            ("H264", 0UL, new byte[] { 0x00, 0x00, 0x01, 0x67, 0x11 }),
            ("I264", 100UL, new byte[] { 0x00, 0x00, 0x01, 0x41, 0x22 }),
            ("H264", 200UL, new byte[] { 0x00, 0x00, 0x01, 0x65, 0x33 }),
            ("I264", 300UL, new byte[] { 0x00, 0x00, 0x01, 0x41, 0x44 }));

        var result = new DatPreviewWindowExtractor().ExtractWindow(
            datPath,
            TimeSpan.FromSeconds(9),
            TimeSpan.FromSeconds(12),
            outputPath);

        Assert.True(result.Succeeded, result.FailureReason);
        Assert.Equal(TimeSpan.FromSeconds(8), result.SelectedKeyframeLocalOffset);
        Assert.Equal(TimeSpan.FromSeconds(1), result.PreviewSeekOffset);
        Assert.True(result.WrittenFrameRecordCount >= 2);
        Assert.True(File.Exists(outputPath));
        Assert.Equal(new byte[] { 0x00, 0x00, 0x01 }, File.ReadAllBytes(outputPath).Take(3).ToArray());
    }

    [Fact]
    public async Task ExtractFrameAsync_FailedFfmpegPreview_PreservesTechnicalDetails()
    {
        using var temp = new TempDirectory();
        var datPath = Path.Combine(temp.Path, "sample.dat");
        WriteDat(
            datPath,
            ("H264", 0UL, new byte[] { 0x00, 0x00, 0x01, 0x67, 0x11 }),
            ("I264", 100UL, new byte[] { 0x00, 0x00, 0x01, 0x41, 0x22 }));
        var timeline = RecordingTimelineBuilder.FromSingleDat(datPath, TimeSpan.FromSeconds(2), null, null);
        using var provider = new TrimPreviewFrameProvider(
            CreateTools(),
            (_, _, _, _) => Task.FromResult(new ProcessRunResult(1, false, false, "", "decode failed")));

        var result = await provider.ExtractFrameAsync(timeline, TimeSpan.FromSeconds(1), FpsOption.FromLabel("30"), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("Source DAT:", result.TechnicalDetails);
        Assert.Contains("Selected keyframe local offset:", result.TechnicalDetails);
        Assert.Contains("FFmpeg command:", result.TechnicalDetails);
        Assert.Contains("Exit code: 1", result.TechnicalDetails);
        Assert.Contains("decode failed", result.TechnicalDetails);
    }

    [Fact]
    public void TrimPreviewState_DateTimeDisplay_CrossesMidnight()
    {
        var timeline = RecordingTimelineBuilder.FromSingleDat(
            @"C:\video\sample.dat",
            TimeSpan.FromMinutes(2),
            new DateTime(2026, 5, 27, 23, 59, 30),
            new DateTime(2026, 5, 28, 0, 1, 30));
        var state = new TrimPreviewState(timeline, null);

        state.SetCurrent(TimeSpan.FromSeconds(45));

        Assert.Equal("2026-05-28 00:00:15", TrimRangeFormatter.FormatOffset(timeline, state.Current));
    }

    [Fact]
    public void TrimPreviewState_ElapsedDisplay_UsesElapsedWhenTimestampsUnknown()
    {
        var timeline = RecordingTimelineBuilder.FromSingleDat(
            @"C:\video\sample.dat",
            TimeSpan.FromMinutes(2),
            recordingStart: null,
            recordingEnd: null);
        var state = new TrimPreviewState(timeline, null);

        state.SetCurrent(TimeSpan.FromSeconds(75));

        Assert.Equal("00:01:15", TrimRangeFormatter.FormatOffset(timeline, state.Current));
    }

    [Fact]
    public void TrimPreviewState_SetStartAndSetEnd_BuildsValidTrimRange()
    {
        var state = new TrimPreviewState(CreateTimeline(), null);

        state.SetCurrent(TimeSpan.FromSeconds(10));
        state.SetStartToCurrent();
        state.SetCurrent(TimeSpan.FromSeconds(30));
        state.SetEndToCurrent();

        Assert.True(state.TryBuildTrimRange(out var range, out var message));
        Assert.Null(message);
        Assert.Equal(new TrimRange(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)), range);
    }

    [Fact]
    public void TrimRangeFormatter_FormatDuration_UsesHoursMinutesSeconds()
    {
        var text = TrimRangeFormatter.FormatDuration(
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(584));

        Assert.Equal("00:09:34", text);
    }

    [Fact]
    public void TrimRangeFormatter_FormatDuration_WithoutStartOrEndShowsFullVideo()
    {
        Assert.Equal("Full video", TrimRangeFormatter.FormatDuration(null, TimeSpan.FromSeconds(30)));
        Assert.Equal("Full video", TrimRangeFormatter.FormatDuration(TimeSpan.FromSeconds(10), null));
        Assert.Equal("Full video", TrimRangeFormatter.FormatDuration(null, null));
    }

    [Fact]
    public void TrimRangeFormatter_FormatDuration_WithEndBeforeStartShowsDash()
    {
        var text = TrimRangeFormatter.FormatDuration(
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(10));

        Assert.Equal("\u2014", text);
    }

    [Fact]
    public void TrimRangeFormatter_FormatPreviewDuration_WithoutStartUsesCurrentOffset()
    {
        var text = TrimRangeFormatter.FormatPreviewDuration(
            TimeSpan.FromSeconds(289),
            start: null,
            end: null);

        Assert.Equal("00:04:49", text);
    }

    [Fact]
    public void TrimRangeFormatter_FormatPreviewDuration_WithStartOnlyUsesCurrentMinusStart()
    {
        var text = TrimRangeFormatter.FormatPreviewDuration(
            TimeSpan.FromSeconds(289),
            TimeSpan.FromSeconds(92),
            end: null);

        Assert.Equal("00:03:17", text);
    }

    [Fact]
    public void TrimRangeFormatter_FormatPreviewDuration_WithStartAndEndIgnoresCurrent()
    {
        var text = TrimRangeFormatter.FormatPreviewDuration(
            TimeSpan.FromSeconds(400),
            TimeSpan.FromSeconds(92),
            TimeSpan.FromSeconds(289));

        Assert.Equal("00:03:17", text);
    }

    [Fact]
    public void TrimRangeFormatter_FormatPreviewDuration_WithCurrentBeforeStartShowsDash()
    {
        var text = TrimRangeFormatter.FormatPreviewDuration(
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(92),
            end: null);

        Assert.Equal("\u2014", text);
    }

    [Fact]
    public void TrimPreviewState_CancelPattern_DoesNotMutateQueueItemTrimRange()
    {
        var item = CreateItem();
        var previous = new TrimRange(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15));
        item.TrimRange = previous;
        var state = new TrimPreviewState(RecordingTimelineBuilder.Build(item), item.TrimRange);

        state.SetCurrent(TimeSpan.FromSeconds(20));
        state.SetStartToCurrent();
        state.SetCurrent(TimeSpan.FromSeconds(40));
        state.SetEndToCurrent();

        Assert.Equal(previous, item.TrimRange);
    }

    [Fact]
    public void TrimPreviewState_ClearTrim_ReturnsToFullVideo()
    {
        var state = new TrimPreviewState(CreateTimeline(), new TrimRange(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15)));

        state.ClearTrim();

        Assert.True(state.TryBuildTrimRange(out var range, out var message));
        Assert.Null(message);
        Assert.Null(range);
    }

    [Fact]
    public void TrimPreviewState_EndBeforeStart_IsRejected()
    {
        var state = new TrimPreviewState(CreateTimeline(), null);
        state.SetCurrent(TimeSpan.FromSeconds(30));
        state.SetStartToCurrent();
        state.SetCurrent(TimeSpan.FromSeconds(10));
        state.SetEndToCurrent();

        Assert.False(state.TryBuildTrimRange(out var range, out var message));
        Assert.Null(range);
        Assert.Equal("End must be after Start.", message);
    }

    [Fact]
    public void TrimTimelineControl_SetMarkers_ShowsStartAndEndMarkers()
    {
        using var control = new TrimTimelineControl();
        control.SetTimeline(TimeSpan.FromMinutes(1));

        control.SetMarkers(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(40));

        Assert.True(control.HasStartMarker);
        Assert.True(control.HasEndMarker);
    }

    [Fact]
    public void TrimTimelineControl_ClearMarkers_RemovesStartAndEndMarkers()
    {
        using var control = new TrimTimelineControl();
        control.SetTimeline(TimeSpan.FromMinutes(1));
        control.SetMarkers(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(40));

        control.SetMarkers(null, null);

        Assert.False(control.HasStartMarker);
        Assert.False(control.HasEndMarker);
    }

    [Fact]
    public void PreviewProvider_Dispose_CleansTempDirectory()
    {
        string tempDirectory;
        using (var provider = new TrimPreviewFrameProvider(CreateTools()))
        {
            tempDirectory = provider.TempDirectory;
            File.WriteAllText(Path.Combine(tempDirectory, "preview.jpg"), "temp");
            Assert.True(Directory.Exists(tempDirectory));
        }

        Assert.False(Directory.Exists(tempDirectory));
    }

    private static RecordingTimeline CreateTimeline()
    {
        return RecordingTimelineBuilder.FromSingleDat(
            @"C:\video\sample.dat",
            TimeSpan.FromMinutes(1),
            recordingStart: null,
            recordingEnd: null);
    }

    private static SpotterSplitExportPlan CreateSplitPlan()
    {
        return new SpotterSplitExportPlan
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
    }

    private static FfmpegTools CreateTools()
    {
        return new FfmpegTools(
            @"C:\app",
            @"C:\app\tools\ffmpeg\ffmpeg.exe",
            @"C:\app\tools\ffmpeg\ffprobe.exe",
            FfmpegExists: true,
            FfprobeExists: true);
    }

    private static QueueItem CreateItem()
    {
        var root = Path.Combine(Path.GetTempPath(), "DatConverter.Tests");
        var item = new QueueItem(
            Path.Combine(root, "sample.dat"),
            Path.Combine(root, "sample.mp4"),
            OutputDestinationMode.SameFolderAsSource,
            null,
            OutputFormat.Mp4,
            "Fast",
            FpsOption.FromLabel("30"),
            hasExistingDirectOutput: false);
        item.PreProbeResult = new ProbeResult(
            true,
            "OK",
            "ffprobe.exe",
            item.Fps,
            Duration: "60");
        return item;
    }

    private static void WriteDat(string path, params (string Kind, ulong Timestamp, byte[] Payload)[] records)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        foreach (var record in records)
        {
            WriteDatRecord(stream, record.Kind, record.Timestamp, record.Payload);
        }
    }

    private static void WriteDatRecord(Stream stream, string kind, ulong timestamp, byte[] payload)
    {
        using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        writer.Write(timestamp);
        writer.Write(1920U);
        writer.Write(1080U);
        writer.Write(System.Text.Encoding.ASCII.GetBytes(kind));
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
