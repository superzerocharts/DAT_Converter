namespace DatConverter.Tests;

public sealed class StoryboardCombinedConversionTests
{
    private const string ValidSegmentProbeJson = "{\"streams\":[{\"codec_name\":\"h264\",\"width\":1920,\"height\":1080,\"r_frame_rate\":\"30/1\",\"avg_frame_rate\":\"30/1\",\"duration\":\"5.000000\"}],\"format\":{\"duration\":\"5.000000\"}}";

    [Fact]
    public void StoryboardCombinedFpsResolver_AutoDetectUsesFinal30AndPreservesPerClipEvidence()
    {
        using var temp = new TempDirectory();
        var plan = CreatePlan(temp.Path, clipCount: 2);
        var decisions = new Dictionary<string, FpsDecisionResult>(StringComparer.OrdinalIgnoreCase)
        {
            [plan.Clips[0].DatFilePath] = AutoDecision(20, "20", 19.772),
            [plan.Clips[1].DatFilePath] = AutoDecision(30, "30", 29.965)
        };
        var resolver = new StoryboardCombinedFpsResolver(
            (path, _) => DetectionFor(path, decisions[path]),
            detection => decisions[detection.TechnicalDetails.Warnings[0]]);

        var resolution = resolver.ResolveCombinedOutputFps(plan, QueueItemFpsSettings.AutoDetect());

        Assert.True(resolution.HasResolvedFps);
        Assert.Equal(FpsSelectionMode.AutoDetect, resolution.SelectionMode);
        Assert.Equal("Auto 30", resolution.DisplayLabel);
        Assert.Equal(30, resolution.NominalConversionFps);
        Assert.Equal("30", resolution.FfmpegRateValue);
        Assert.Contains("Final combined output FPS: Auto 30", resolution.TechnicalLogText);
        Assert.Contains("Clip 1", resolution.TechnicalLogText);
        Assert.Contains("detected=Auto 20 fps", resolution.TechnicalLogText);
        Assert.Contains("Clip 2", resolution.TechnicalLogText);
        Assert.Contains("detected=Auto 30 fps", resolution.TechnicalLogText);
    }

    [Fact]
    public void StoryboardCombinedFpsResolver_Manual20UsesManualFinal20()
    {
        using var temp = new TempDirectory();
        var plan = CreatePlan(temp.Path, clipCount: 2);
        var resolver = new StoryboardCombinedFpsResolver();

        var resolution = resolver.ResolveCombinedOutputFps(
            plan,
            QueueItemFpsSettings.FromManual(FpsOption.FromLabel("20")));

        Assert.Equal(FpsSelectionMode.Manual, resolution.SelectionMode);
        Assert.Equal("20", resolution.DisplayLabel);
        Assert.Equal(20, resolution.NominalConversionFps);
        Assert.Equal("20", resolution.FfmpegRateValue);
        Assert.Contains("Final combined FFmpeg FPS value: 20", resolution.TechnicalLogText);
    }

    [Fact]
    public void StoryboardCombinedFpsResolver_Manual2997UsesNtscFfmpegValue()
    {
        using var temp = new TempDirectory();
        var plan = CreatePlan(temp.Path, clipCount: 2);
        var resolver = new StoryboardCombinedFpsResolver();

        var resolution = resolver.ResolveCombinedOutputFps(
            plan,
            QueueItemFpsSettings.FromManual(FpsOption.FromLabel("29.97")));

        Assert.Equal("29.97", resolution.DisplayLabel);
        Assert.Equal(29.97, resolution.NominalConversionFps);
        Assert.Equal("30000/1001", resolution.FfmpegRateValue);
        Assert.Contains("Final combined FFmpeg FPS value: 30000/1001", resolution.TechnicalLogText);
    }

    [Fact]
    public async Task EncodeCombinedStoryboardAsync_ExtractsAllClipsInOrderAndCleansTempFiles()
    {
        using var temp = new TempDirectory();
        var plan = CreatePlan(temp.Path, clipCount: 3);
        var outputPath = Path.Combine(temp.Path, "Storyboard.mp4");
        var extractedInputs = new List<string>();
        var ffmpegArguments = new List<IReadOnlyList<string>>();
        var progressUpdates = new List<ConversionProgress>();
        var service = new ConversionService(
            new FfmpegTools(AppContext.BaseDirectory, "ffmpeg.exe", "ffprobe.exe", FfmpegExists: true, FfprobeExists: true),
            InternalConversionPathOptions.Default,
            (inputPath, outputH264Path, _) =>
            {
                extractedInputs.Add(inputPath);
                File.WriteAllBytes(outputH264Path, [0, 0, 0, 1, 0x67, 0, 0, 0, 1, 0x68, 0, 0, 0, 1, 0x65]);
                return CreateExtractionResult(inputPath, outputH264Path);
            },
            (executable, arguments, _, _, stdout, _) =>
            {
                if (string.Equals(executable, "ffprobe.exe", StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(new ProcessRunResult(0, false, false, ValidSegmentProbeJson, ""));
                }

                ffmpegArguments.Add(arguments);
                var destination = arguments[^1];
                File.WriteAllText(destination, "video");
                stdout?.Invoke("out_time_ms=1000000");
                stdout?.Invoke("progress=end");
                return Task.FromResult(new ProcessRunResult(0, false, false, "", ""));
            });

        var result = await service.EncodeCombinedStoryboardAsync(
            plan,
            outputPath,
            OutputFormat.Mp4,
            FpsOption.FromLabel("30"),
            TimeSpan.FromSeconds(30),
            null,
            new CaptureProgress(progressUpdates),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.StandardError);
        Assert.Equal(ConversionInputPathMode.StoryboardCombinedCleanH264, result.InputPathMode);
        Assert.Equal(plan.Clips.Select(clip => clip.DatFilePath), extractedInputs);
        Assert.Equal(4, ffmpegArguments.Count);
        Assert.All(ffmpegArguments.Take(3), args =>
        {
            AssertFfmpegOption(args, "-r", "30");
            Assert.Contains("fps=30", string.Join(" ", args));
        });
        Assert.Contains("fps=30", string.Join(" ", ffmpegArguments[^1]));
        Assert.Contains(ffmpegArguments.Take(3), args => args.Any(argument => argument.Contains("scale=1920:1080:force_original_aspect_ratio=decrease,pad=1920:1080:(ow-iw)/2:(oh-ih)/2", StringComparison.Ordinal)));
        Assert.Contains(ffmpegArguments.Take(3), args => args.Any(argument => argument.Contains("fps=30,format=yuv420p", StringComparison.Ordinal)));
        Assert.DoesNotContain("-f", ffmpegArguments[^1]);
        Assert.DoesNotContain("concat.txt", string.Join(" ", ffmpegArguments[^1]));
        Assert.Contains("-filter_complex", ffmpegArguments[^1]);
        Assert.Contains("concat=n=3:v=1:a=0", string.Join(" ", ffmpegArguments[^1]));
        AssertFfmpegOption(ffmpegArguments[^1], "-map", "[v]");
        Assert.Contains(progressUpdates, progress => progress.Summary == "Extracting storyboard segment 1 of 3..." && progress.Percent.HasValue);
        Assert.Contains(progressUpdates, progress => progress.Summary == "Normalizing storyboard segment 1 of 3..." && progress.Percent.HasValue);
        Assert.Contains(progressUpdates, progress => progress.Summary == "Preparing storyboard merge..." && progress.Percent.HasValue);
        Assert.Contains(progressUpdates, progress => progress.Summary == "Finalizing storyboard video..." && progress.Percent.HasValue);
        Assert.False(Directory.EnumerateDirectories(temp.Path, "*.storyboard-combine").Any());
    }

    [Fact]
    public void WriteStoryboardConcatList_WritesUtf8WithoutBomAndEscapesPaths()
    {
        using var temp = new TempDirectory();
        var firstClip = Path.Combine(temp.Path, "clip 1.mp4");
        var secondClip = Path.Combine(temp.Path, "O'Brien clip.mp4");
        File.WriteAllText(firstClip, "video");
        File.WriteAllText(secondClip, "video");
        var concatPath = Path.Combine(temp.Path, "concat.txt");

        ConversionService.WriteStoryboardConcatList(concatPath, [firstClip, secondClip]);

        var bytes = File.ReadAllBytes(concatPath);
        Assert.True(bytes.Length >= 4);
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
        Assert.Equal((byte)'f', bytes[0]);
        Assert.Equal((byte)'i', bytes[1]);
        Assert.Equal((byte)'l', bytes[2]);
        Assert.Equal((byte)'e', bytes[3]);

        var lines = File.ReadAllLines(concatPath);
        Assert.StartsWith("file ", lines.First(line => !string.IsNullOrWhiteSpace(line)), StringComparison.Ordinal);
        Assert.Contains("clip 1.mp4", lines[0]);
        Assert.Contains("O'\\''Brien clip.mp4", lines[1]);

        var validation = ConversionService.ValidateStoryboardConcatList(concatPath, [firstClip, secondClip]);
        Assert.True(validation.IsValid, validation.Message);
    }

    [Fact]
    public async Task EncodeCombinedStoryboardAsync_WithTrimRangeTrimsIntermediateSegmentNotFinalConcat()
    {
        using var temp = new TempDirectory();
        var plan = CreatePlan(temp.Path, clipCount: 1);
        var outputPath = Path.Combine(temp.Path, "Storyboard_trim.mp4");
        var ffmpegArguments = new List<IReadOnlyList<string>>();
        var service = new ConversionService(
            new FfmpegTools(AppContext.BaseDirectory, "ffmpeg.exe", "ffprobe.exe", FfmpegExists: true, FfprobeExists: true),
            InternalConversionPathOptions.Default,
            (inputPath, outputH264Path, _) =>
            {
                File.WriteAllBytes(outputH264Path, [0, 0, 0, 1, 0x67, 0, 0, 0, 1, 0x68, 0, 0, 0, 1, 0x65]);
                return CreateExtractionResult(inputPath, outputH264Path);
            },
            (executable, arguments, _, _, stdout, _) =>
            {
                if (string.Equals(executable, "ffprobe.exe", StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(new ProcessRunResult(0, false, false, ValidSegmentProbeJson, ""));
                }

                ffmpegArguments.Add(arguments);
                File.WriteAllBytes(arguments[^1], new byte[262]);
                stdout?.Invoke("progress=end");
                return Task.FromResult(new ProcessRunResult(0, false, false, "", ""));
            });

        var result = await service.EncodeCombinedStoryboardAsync(
            plan,
            outputPath,
            OutputFormat.Mp4,
            FpsOption.FromLabel("30"),
            TimeSpan.FromSeconds(6),
            new TrimRange(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(8)),
            progress: null,
            cancellationToken: CancellationToken.None);

        Assert.True(result.IsSuccess, result.StandardError);
        var intermediateArguments = ffmpegArguments[0];
        AssertFfmpegOption(intermediateArguments, "-ss", "2");
        AssertFfmpegOption(intermediateArguments, "-t", "6");
        var finalArguments = ffmpegArguments[^1];
        Assert.DoesNotContain("-ss", finalArguments);
        Assert.DoesNotContain("-t", finalArguments);
    }

    [Fact]
    public async Task EncodeCombinedStoryboardAsync_FailsBeforeFinalMergeWhenNormalizedSegmentHasNoVideoStream()
    {
        using var temp = new TempDirectory();
        var plan = CreatePlan(temp.Path, clipCount: 1);
        var outputPath = Path.Combine(temp.Path, "Storyboard.mp4");
        var ffmpegArguments = new List<IReadOnlyList<string>>();
        var service = new ConversionService(
            new FfmpegTools(AppContext.BaseDirectory, "ffmpeg.exe", "ffprobe.exe", FfmpegExists: true, FfprobeExists: true),
            InternalConversionPathOptions.Default,
            (inputPath, outputH264Path, _) =>
            {
                File.WriteAllBytes(outputH264Path, [0, 0, 0, 1, 0x67, 0, 0, 0, 1, 0x68, 0, 0, 0, 1, 0x65]);
                return CreateExtractionResult(inputPath, outputH264Path);
            },
            (executable, arguments, _, _, stdout, _) =>
            {
                if (string.Equals(executable, "ffprobe.exe", StringComparison.OrdinalIgnoreCase))
                {
                    return arguments.Contains("-f")
                        ? Task.FromResult(new ProcessRunResult(0, false, false, ValidSegmentProbeJson, ""))
                        : Task.FromResult(new ProcessRunResult(0, false, false, "{\"streams\":[]}", ""));
                }

                ffmpegArguments.Add(arguments);
                File.WriteAllText(arguments[^1], "video");
                stdout?.Invoke("progress=end");
                return Task.FromResult(new ProcessRunResult(0, false, false, "", ""));
            });

        var result = await service.EncodeCombinedStoryboardAsync(
            plan,
            outputPath,
            OutputFormat.Mp4,
            FpsOption.FromLabel("30"),
            TimeSpan.FromSeconds(10),
            null,
            progress: null,
            cancellationToken: CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("Storyboard merge failed while preparing video segments.", result.UserMessage);
        Assert.Single(ffmpegArguments);
        Assert.Contains("Normalized storyboard segment validation", result.StandardError);
        Assert.Contains("output_bytes=", result.StandardError);
        Assert.Contains("video_stream_found=no", result.StandardError);
        Assert.Contains("Storyboard temp files preserved for troubleshooting:", result.StandardError);
        Assert.True(Directory.EnumerateDirectories(temp.Path, "*.storyboard-combine").Any());
    }

    [Fact]
    public async Task EncodeCombinedStoryboardAsync_LogsNormalizedSegmentValidationForValidSegments()
    {
        using var temp = new TempDirectory();
        var plan = CreatePlan(temp.Path, clipCount: 2);
        var outputPath = Path.Combine(temp.Path, "Storyboard.mp4");
        var service = new ConversionService(
            new FfmpegTools(AppContext.BaseDirectory, "ffmpeg.exe", "ffprobe.exe", FfmpegExists: true, FfprobeExists: true),
            InternalConversionPathOptions.Default,
            (inputPath, outputH264Path, _) =>
            {
                File.WriteAllBytes(outputH264Path, [0, 0, 0, 1, 0x67, 0, 0, 0, 1, 0x68, 0, 0, 0, 1, 0x65]);
                return CreateExtractionResult(inputPath, outputH264Path);
            },
            (executable, arguments, _, _, stdout, _) =>
            {
                if (string.Equals(executable, "ffprobe.exe", StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(new ProcessRunResult(0, false, false, ValidSegmentProbeJson, ""));
                }

                File.WriteAllText(arguments[^1], "video");
                stdout?.Invoke("progress=end");
                return Task.FromResult(new ProcessRunResult(0, false, false, "", ""));
            });

        var result = await service.EncodeCombinedStoryboardAsync(
            plan,
            outputPath,
            OutputFormat.Mp4,
            FpsOption.FromLabel("30"),
            TimeSpan.FromSeconds(20),
            null,
            progress: null,
            cancellationToken: CancellationToken.None);

        Assert.True(result.IsSuccess, result.StandardError);
        Assert.Contains("Normalized storyboard segment validation", result.StandardError);
        Assert.Contains("video_stream_found=yes", result.StandardError);
        Assert.Contains("codec=h264", result.StandardError);
        Assert.Contains("resolution=1920x1080", result.StandardError);
        Assert.Contains("duration=5.000000", result.StandardError);
        Assert.Contains("frame_rate=30/1", result.StandardError);
    }

    [Fact]
    public void BuildStoryboardConcatEncodeArguments_TwoSegmentsUsesExplicitInputsConcatFilterAndMap()
    {
        var arguments = FfmpegCommandBuilder.BuildStoryboardConcatEncodeArguments(
            [@"C:\temp\segment-001.mp4", @"C:\temp\segment-002.mp4"],
            @"C:\temp\out.mp4",
            OutputFormat.Mp4,
            FpsOption.FromLabel("30"));

        Assert.Equal(2, arguments.Count(argument => argument == "-i"));
        Assert.Contains(@"C:\temp\segment-001.mp4", arguments);
        Assert.Contains(@"C:\temp\segment-002.mp4", arguments);
        AssertFfmpegOption(arguments, "-filter_complex", "[0:v:0][1:v:0]concat=n=2:v=1:a=0,fps=30,format=yuv420p[v]");
        AssertFfmpegOption(arguments, "-map", "[v]");
        AssertFfmpegOption(arguments, "-movflags", "+faststart");
    }

    [Fact]
    public void BuildStoryboardConcatEncodeArguments_OneSegmentUsesExplicitMapWithoutConcatFilter()
    {
        var arguments = FfmpegCommandBuilder.BuildStoryboardConcatEncodeArguments(
            [@"C:\temp\segment-001.mp4"],
            @"C:\temp\out.mp4",
            OutputFormat.Mp4,
            FpsOption.FromLabel("30"));

        Assert.Single(arguments, argument => argument == "-i");
        Assert.DoesNotContain("-filter_complex", arguments);
        Assert.DoesNotContain("concat=n=2", string.Join(" ", arguments));
        AssertFfmpegOption(arguments, "-filter:v", "fps=30,format=yuv420p");
        AssertFfmpegOption(arguments, "-map", "0:v:0");
    }

    [Fact]
    public void BuildStoryboardConcatEncodeArguments_MkvOmitsFaststart()
    {
        var arguments = FfmpegCommandBuilder.BuildStoryboardConcatEncodeArguments(
            [@"C:\temp\segment-001.mp4", @"C:\temp\segment-002.mp4"],
            @"C:\temp\out.mkv",
            OutputFormat.Mkv,
            FpsOption.FromLabel("30"));

        Assert.DoesNotContain("-movflags", arguments);
        Assert.Contains("-map", arguments);
    }

    [Fact]
    public void StoryboardTrimSegmentPlanner_TrimInsideOneClipBuildsOneSegment()
    {
        using var temp = new TempDirectory();
        var plan = CreatePlan(temp.Path, clipCount: 3);

        var segmentPlan = StoryboardTrimSegmentPlanner.Build(
            plan,
            new TrimRange(TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(18)));

        var segment = Assert.Single(segmentPlan.Segments);
        Assert.Equal(2, segment.ClipNumber);
        Assert.Equal(TimeSpan.FromSeconds(2), segment.LocalStartOffset);
        Assert.Equal(TimeSpan.FromSeconds(6), segment.LocalDuration);
        Assert.Equal(2, segmentPlan.SkippedClipCount);
    }

    [Fact]
    public void StoryboardTrimSegmentPlanner_CrossingClipBoundaryBuildsOrderedSegments()
    {
        using var temp = new TempDirectory();
        var plan = CreatePlan(temp.Path, clipCount: 6);

        var segmentPlan = StoryboardTrimSegmentPlanner.Build(
            plan,
            new TrimRange(TimeSpan.FromSeconds(45), TimeSpan.FromSeconds(55)));

        Assert.Equal([5, 6], segmentPlan.Segments.Select(segment => segment.ClipNumber).ToArray());
        Assert.Equal(4, segmentPlan.SkippedClipCount);
        Assert.Equal(TimeSpan.FromSeconds(5), segmentPlan.Segments[0].LocalStartOffset);
        Assert.Equal(TimeSpan.FromSeconds(5), segmentPlan.Segments[0].LocalDuration);
        Assert.Equal(TimeSpan.Zero, segmentPlan.Segments[1].LocalStartOffset);
        Assert.Equal(TimeSpan.FromSeconds(5), segmentPlan.Segments[1].LocalDuration);
    }

    [Fact]
    public void StoryboardTrimSegmentPlanner_UsesSameTimelineBasisAsTrimPreview()
    {
        using var temp = new TempDirectory();
        var plan = CreatePlan(temp.Path, clipCount: 3);
        var item = new QueueItem(
            plan.SidecarPath,
            Path.Combine(temp.Path, "Storyboard.mp4"),
            OutputDestinationMode.SameFolderAsSource,
            null,
            OutputFormat.Mp4,
            ConversionModes.Encode,
            FpsOption.FromLabel("30"),
            hasExistingDirectOutput: false)
        {
            IsCombinedStoryboard = true,
            StoryboardPlan = plan
        };

        var previewTimeline = RecordingTimelineBuilder.Build(item);
        var segmentPlan = StoryboardTrimSegmentPlanner.Build(
            plan,
            new TrimRange(TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(18)));

        Assert.Equal(TimeSpan.Zero, previewTimeline.Segments[0].ElapsedOffset);
        Assert.Equal(TimeSpan.FromSeconds(10), previewTimeline.Segments[1].ElapsedOffset);
        Assert.Equal(StoryboardTrimSegmentPlanner.TimelineMode, segmentPlan.TimelineMode);
        Assert.True(StoryboardTimelineDiagnosticBuilder.PlannerUsesTrimPreviewTimelineBasis(segmentPlan));
    }

    [Fact]
    public void StoryboardTrimSegmentPlanner_BoundarySpanningTrimCalculatesBothLocalRanges()
    {
        using var temp = new TempDirectory();
        var plan = CreatePlan(temp.Path, clipCount: 2);

        var segmentPlan = StoryboardTrimSegmentPlanner.Build(
            plan,
            new TrimRange(TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(12)));

        Assert.Equal([1, 2], segmentPlan.Segments.Select(segment => segment.ClipNumber).ToArray());
        Assert.Equal(TimeSpan.FromSeconds(8), segmentPlan.Segments[0].LocalStartOffset);
        Assert.Equal(TimeSpan.FromSeconds(2), segmentPlan.Segments[0].LocalDuration);
        Assert.Equal(TimeSpan.Zero, segmentPlan.Segments[1].LocalStartOffset);
        Assert.Equal(TimeSpan.FromSeconds(2), segmentPlan.Segments[1].LocalDuration);
    }

    [Fact]
    public void StoryboardTrimSegmentPlanner_NoIntersectingClipsBuildsEmptyPlan()
    {
        using var temp = new TempDirectory();
        var plan = CreatePlan(temp.Path, clipCount: 2);

        var segmentPlan = StoryboardTrimSegmentPlanner.Build(
            plan,
            new TrimRange(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(40)));

        Assert.Empty(segmentPlan.Segments);
        Assert.Equal(2, segmentPlan.SkippedClipCount);
    }

    [Fact]
    public async Task EncodeCombinedStoryboardAsync_WithBoundaryTrimOnlyProcessesIntersectingClips()
    {
        using var temp = new TempDirectory();
        var plan = CreatePlan(temp.Path, clipCount: 6);
        plan.Clips[4].SourceFpsLabel = "Auto 20 fps";
        plan.Clips[4].SourceFfmpegFpsValue = "20";
        plan.Clips[4].SourceFpsDecisionReason = "Stable bucket evidence supports 20 fps.";
        var outputPath = Path.Combine(temp.Path, "Storyboard_trim.mp4");
        var extractedInputs = new List<string>();
        var ffmpegArguments = new List<IReadOnlyList<string>>();
        var service = new ConversionService(
            new FfmpegTools(AppContext.BaseDirectory, "ffmpeg.exe", "ffprobe.exe", FfmpegExists: true, FfprobeExists: true),
            InternalConversionPathOptions.Default,
            (inputPath, outputH264Path, _) =>
            {
                extractedInputs.Add(inputPath);
                File.WriteAllBytes(outputH264Path, [0, 0, 0, 1, 0x67, 0, 0, 0, 1, 0x68, 0, 0, 0, 1, 0x65]);
                return CreateExtractionResult(inputPath, outputH264Path);
            },
            (executable, arguments, _, _, stdout, _) =>
            {
                if (string.Equals(executable, "ffprobe.exe", StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(new ProcessRunResult(0, false, false, ValidSegmentProbeJson, ""));
                }

                ffmpegArguments.Add(arguments);
                File.WriteAllText(arguments[^1], "video");
                stdout?.Invoke("progress=end");
                return Task.FromResult(new ProcessRunResult(0, false, false, "", ""));
            });

        var result = await service.EncodeCombinedStoryboardAsync(
            plan,
            outputPath,
            OutputFormat.Mp4,
            FpsOption.FromLabel("30"),
            TimeSpan.FromSeconds(10),
            new TrimRange(TimeSpan.FromSeconds(45), TimeSpan.FromSeconds(55)),
            progress: null,
            cancellationToken: CancellationToken.None);

        Assert.True(result.IsSuccess, result.StandardError);
        Assert.Equal(plan.Clips.Skip(4).Take(2).Select(clip => clip.DatFilePath), extractedInputs);
        Assert.Equal(3, ffmpegArguments.Count);
        AssertFfmpegOption(ffmpegArguments[0], "-r", "20");
        AssertFfmpegOption(ffmpegArguments[0], "-ss", "5");
        AssertFfmpegOption(ffmpegArguments[0], "-t", "5");
        Assert.Contains("fps=30", string.Join(" ", ffmpegArguments[0]));
        AssertFfmpegOption(ffmpegArguments[1], "-r", "30");
        AssertFfmpegOption(ffmpegArguments[1], "-t", "5");
        Assert.Contains("fps=30", string.Join(" ", ffmpegArguments[1]));
        Assert.DoesNotContain("-ss", ffmpegArguments[^1]);
        Assert.DoesNotContain("-t", ffmpegArguments[^1]);
        Assert.Contains("Storyboard Timeline Diagnostic", result.StandardError);
        Assert.Contains("Segment 1: source_clip_index=5", result.StandardError);
        Assert.Contains("Segment 2: source_clip_index=6", result.StandardError);
        Assert.Contains("trim dialog and merge planner basis match: Yes", result.StandardError);
        Assert.Contains("Segment 1 normalize: clip 5; source_input_fps=Auto 20 fps (20); final_output_fps=30 (30)", result.StandardError);
        Assert.Contains("Segment 2 normalize: clip 6; source_input_fps=Auto 30 fps (30); final_output_fps=30 (30)", result.StandardError);
    }

    [Fact]
    public void StoryboardTimelineDiagnosticBuilder_IncludesRequiredSections()
    {
        using var temp = new TempDirectory();
        var plan = CreatePlan(temp.Path, clipCount: 3);
        var segmentPlan = StoryboardTrimSegmentPlanner.Build(
            plan,
            new TrimRange(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)));
        var normalizedPaths = segmentPlan.Segments
            .Select(segment => Path.Combine(temp.Path, $"segment-{segment.OutputSegmentNumber:000}.mp4"))
            .ToList();

        var diagnostic = StoryboardTimelineDiagnosticBuilder.Build(
            plan,
            new TrimRange(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)),
            segmentPlan,
            normalizedPaths,
            _ => new SpotterFpsDetectionResult
            {
                Succeeded = true,
                TechnicalDetails = new SpotterFpsTechnicalDetails
                {
                    Width = 1920,
                    Height = 1080,
                    AverageFps = 30,
                    BucketMedianFps = 30
                }
            });

        Assert.Contains("Storyboard Timeline Diagnostic", diagnostic);
        Assert.Contains("Selected trim range:", diagnostic);
        Assert.Contains("value basis: storyboard timeline offset", diagnostic);
        Assert.Contains("Storyboard clip table:", diagnostic);
        Assert.Contains("Intersecting segment plan:", diagnostic);
        Assert.Contains("Skipped clips:", diagnostic);
        Assert.Contains("Final concat plan:", diagnostic);
        Assert.Contains("Safety warnings:", diagnostic);
        Assert.Contains("Selected trim currently intersects only one storyboard segment according to the planner.", diagnostic);
        Assert.Contains("fps=30", diagnostic);
        Assert.Contains("resolution=1920x1080", diagnostic);
    }

    [Fact]
    public void QueuePreProbeService_SkipsCombinedStoryboardItems()
    {
        var item = new QueueItem(
            @"C:\video\Storyboard.sef2",
            @"C:\video\Storyboard.mp4",
            OutputDestinationMode.SameFolderAsSource,
            null,
            OutputFormat.Mp4,
            ConversionModes.Encode,
            FpsOption.FromLabel("30"),
            hasExistingDirectOutput: false)
        {
            IsCombinedStoryboard = true,
            StoryboardPlan = new SpotterStoryboardPlan { Clips = [new SpotterStoryboardClip()] },
            Status = QueueItemStatus.WaitingForProbe
        };

        Assert.False(QueuePreProbeService.ShouldPreProbe(item));
    }

    [Fact]
    public void RecordingTimelineBuilder_CombinedStoryboardUsesStoryboardOrderDuration()
    {
        var first = @"C:\video\first.dat";
        var second = @"C:\video\second.dat";
        var item = new QueueItem(
            @"C:\video\Storyboard.sef2",
            @"C:\video\Storyboard.mp4",
            OutputDestinationMode.SameFolderAsSource,
            null,
            OutputFormat.Mp4,
            ConversionModes.Encode,
            FpsOption.FromLabel("30"),
            hasExistingDirectOutput: false)
        {
            IsCombinedStoryboard = true,
            StoryboardPlan = new SpotterStoryboardPlan
            {
                Clips =
                [
                    new SpotterStoryboardClip { ClipNumber = 1, DatFilePath = first, MaterialStartTime = new DateTime(2026, 1, 1, 0, 0, 0), MaterialEndTime = new DateTime(2026, 1, 1, 0, 0, 10) },
                    new SpotterStoryboardClip { ClipNumber = 2, DatFilePath = second, MaterialStartTime = new DateTime(2026, 1, 1, 0, 1, 0), MaterialEndTime = new DateTime(2026, 1, 1, 0, 1, 5) }
                ]
            }
        };

        var timeline = RecordingTimelineBuilder.Build(item);

        Assert.Equal(TimeSpan.FromSeconds(15), timeline.TotalDuration);
        Assert.Equal(first, timeline.Segments[0].SourcePath);
        Assert.Equal(second, timeline.Segments[1].SourcePath);
        Assert.Equal(TimeSpan.FromSeconds(10), timeline.Segments[1].ElapsedOffset);
    }

    private static SpotterStoryboardPlan CreatePlan(string folder, int clipCount)
    {
        var clips = new List<SpotterStoryboardClip>();
        for (var index = 1; index <= clipCount; index++)
        {
            var datPath = Path.Combine(folder, $"clip{index}.dat");
            File.WriteAllText(datPath, "source");
            clips.Add(new SpotterStoryboardClip
            {
                ClipNumber = index,
                ClipName = $"Clip {index}",
                DatFileName = Path.GetFileName(datPath),
                DatFilePath = datPath,
                MaterialStartTime = DateTime.Today.AddSeconds((index - 1) * 10),
                MaterialEndTime = DateTime.Today.AddSeconds(index * 10),
                SourceFpsLabel = "Auto 30 fps",
                SourceFfmpegFpsValue = "30",
                SourceFpsDecisionReason = "Test default source FPS."
            });
        }

        var sidecarPath = Path.Combine(folder, "Storyboard.sef2");
        File.WriteAllText(sidecarPath, "<archive2><StoryboardData /></archive2>");
        return new SpotterStoryboardPlan
        {
            ExportFolder = folder,
            SidecarPath = sidecarPath,
            StoryboardName = "Storyboard",
            Clips = clips
        };
    }

    private static SpotterDatPayloadExtractionResult CreateExtractionResult(string inputPath, string outputPath)
    {
        return new SpotterDatPayloadExtractionResult
        {
            Succeeded = true,
            InputPath = inputPath,
            OutputPath = outputPath,
            InputFileSize = 100,
            ExtractedPayloadByteCount = 15,
            FrameRecordCount = 1,
            ExtractedFrameRecordCount = 1,
            CandidateNalUnitCount = 3,
            SpsCount = 1,
            PpsCount = 1,
            IdrFrameCount = 1,
            LookedConfident = true
        };
    }

    private static SpotterFpsDetectionResult DetectionFor(string path, FpsDecisionResult decision)
    {
        return new SpotterFpsDetectionResult
        {
            Succeeded = true,
            DetectionSource = "DatFrameRecordsDefaultTimebase",
            Confidence = decision.Confidence,
            TechnicalDetails = new SpotterFpsTechnicalDetails
            {
                FrameCount = 100,
                AverageFps = decision.RawAverageFps,
                BucketMedianFps = decision.NominalConversionFps,
                BucketModeFps = (int?)decision.NominalConversionFps,
                Warnings = [path]
            }
        };
    }

    private static FpsDecisionResult AutoDecision(double fps, string ffmpegValue, double averageFps)
    {
        return new FpsDecisionResult
        {
            AutoDetectionSucceeded = true,
            ShouldUseDetectedRate = true,
            RawAverageFps = averageFps,
            RawBucketMedianFps = fps,
            NominalConversionFps = fps,
            FfmpegRateValue = ffmpegValue,
            UserFacingLabel = $"Auto {fps:0.##} fps",
            Confidence = "Medium",
            DecisionReason = $"Stable bucket evidence supports {fps:0.##} fps.",
            TechnicalLogText = ""
        };
    }

    private static void AssertFfmpegOption(IReadOnlyList<string> arguments, string option, string value)
    {
        var index = arguments.ToList().IndexOf(option);
        Assert.True(index >= 0, $"Expected option {option} in {string.Join(" ", arguments)}");
        Assert.True(index + 1 < arguments.Count, $"Expected value after {option} in {string.Join(" ", arguments)}");
        Assert.Equal(value, arguments[index + 1]);
    }

    private sealed class CaptureProgress(List<ConversionProgress> updates) : IProgress<ConversionProgress>
    {
        public void Report(ConversionProgress value)
        {
            updates.Add(value);
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
