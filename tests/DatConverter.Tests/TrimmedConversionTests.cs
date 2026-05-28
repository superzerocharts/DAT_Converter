namespace DatConverter.Tests;

public sealed class TrimmedConversionTests
{
    [Fact]
    public async Task RemuxAsync_NoTrim_UsesExistingFullCleanRemuxPath()
    {
        using var temp = new TempDirectory();
        var inputPath = Path.Combine(temp.Path, "clip.dat");
        var outputPath = Path.Combine(temp.Path, "clip.mp4");
        File.WriteAllText(inputPath, "source");
        var trimExtractorCalled = false;
        var service = CreateService(
            (_, tempH264Path, _) =>
            {
                File.WriteAllText(tempH264Path, "full clean");
                return CreateExtractionResult(inputPath, tempH264Path);
            },
            (_, _, _, _, _, _) =>
            {
                File.WriteAllText(outputPath, "output");
                return Task.FromResult(new ProcessRunResult(0, false, false, "", ""));
            },
            (_, _, _, _, _, _) =>
            {
                trimExtractorCalled = true;
                throw new InvalidOperationException("Trim extractor should not run.");
            });

        var result = await service.RemuxAsync(inputPath, outputPath, OutputFormat.Mp4, FpsOption.FromLabel("30"), TimeSpan.FromSeconds(10), null, CancellationToken.None);

        Assert.True(result.IsSuccess, result.StandardError);
        Assert.Equal(ConversionInputPathMode.CleanExtractedH264, result.InputPathMode);
        Assert.False(trimExtractorCalled);
    }

    [Fact]
    public async Task EncodeAsync_NoTrim_UsesExistingWholeDatEncodePath()
    {
        using var temp = new TempDirectory();
        var inputPath = Path.Combine(temp.Path, "clip.dat");
        var outputPath = Path.Combine(temp.Path, "clip.mp4");
        File.WriteAllText(inputPath, "source");
        var trimExtractorCalled = false;
        var service = CreateService(
            (_, _, _) => throw new InvalidOperationException("Full clean extractor should not run."),
            (_, arguments, _, _, _, _) =>
            {
                Assert.Equal(inputPath, GetOptionValue(arguments, "-i"));
                File.WriteAllText(outputPath, "output");
                return Task.FromResult(new ProcessRunResult(0, false, false, "", ""));
            },
            (_, _, _, _, _, _) =>
            {
                trimExtractorCalled = true;
                throw new InvalidOperationException("Trim extractor should not run.");
            });

        var result = await service.EncodeAsync(inputPath, outputPath, OutputFormat.Mp4, FpsOption.FromLabel("30"), TimeSpan.FromSeconds(10), null, CancellationToken.None);

        Assert.True(result.IsSuccess, result.StandardError);
        Assert.Equal(ConversionInputPathMode.StandardWholeDatRawH264, result.InputPathMode);
        Assert.False(trimExtractorCalled);
    }

    [Fact]
    public void DatTrimExtraction_SingleDat_WritesOnlySelectedKeyframeAlignedPayloadRange()
    {
        using var temp = new TempDirectory();
        var inputPath = Path.Combine(temp.Path, "clip.dat");
        var outputPath = Path.Combine(temp.Path, "trim.h264");
        WriteDat(
            inputPath,
            ("H264", 0UL, Payload(0x67, 0x10)),
            ("I264", 100UL, Payload(0x41, 0x20)),
            ("H264", 200UL, Payload(0x65, 0x30)),
            ("I264", 300UL, Payload(0x41, 0x40)));

        var result = new DatPreviewWindowExtractor().ExtractRange(
            inputPath,
            TimeSpan.FromSeconds(9),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(12),
            outputPath);

        Assert.True(result.Succeeded, result.FailureReason);
        Assert.Equal(TimeSpan.FromSeconds(8), result.SelectedKeyframeLocalOffset);
        var bytes = File.ReadAllBytes(outputPath);
        Assert.Contains((byte)0x30, bytes);
        Assert.DoesNotContain((byte)0x10, bytes);
    }

    [Fact]
    public async Task RemuxTrimmedAsync_UsesBundledFfmpegAgainstTrimmedTempAndDeletesTemp()
    {
        using var temp = new TempDirectory();
        var inputPath = Path.Combine(temp.Path, "clip.dat");
        var outputPath = Path.Combine(temp.Path, "clip.mp4");
        File.WriteAllText(inputPath, "source");
        string? tempInputPath = null;
        string? tempTrimPath = null;
        var service = CreateService(
            (_, _, _) => throw new InvalidOperationException("Full extractor should not run."),
            (ffmpegPath, arguments, _, _, _, _) =>
            {
                Assert.Equal("ffmpeg.exe", ffmpegPath);
                tempInputPath = GetOptionValue(arguments, "-i");
                File.WriteAllText(outputPath, "trimmed output");
                return Task.FromResult(new ProcessRunResult(0, false, false, "", ""));
            },
            (_, start, end, _, tempH264Path, _) =>
            {
                tempTrimPath = tempH264Path;
                Assert.Equal(TimeSpan.FromSeconds(2), start);
                Assert.Equal(TimeSpan.FromSeconds(5), end);
                File.WriteAllText(tempH264Path, "trimmed h264");
                return CreateWindowResult(tempH264Path, start, keyframe: TimeSpan.FromSeconds(1));
            });

        var result = await service.RemuxTrimmedAsync(inputPath, outputPath, OutputFormat.Mp4, FpsOption.FromLabel("30"), TimeSpan.FromSeconds(10), new TrimRange(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5)), null, CancellationToken.None);

        Assert.True(result.IsSuccess, result.StandardError);
        Assert.Equal(ConversionInputPathMode.TrimmedCleanH264, result.InputPathMode);
        Assert.Equal(tempTrimPath, tempInputPath);
        Assert.NotNull(tempTrimPath);
        Assert.False(File.Exists(tempTrimPath!));
        Assert.Contains("keyframe", result.StandardError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RemuxTrimmedSplitAsync_CrossingBoundary_ExtractsOnlyRelevantSegmentPortions()
    {
        using var temp = new TempDirectory();
        var plan = CreateSplitPlan(temp.Path);
        var timeline = RecordingTimelineBuilder.FromSplitExportPlan(plan, plan.Segments[0].FilePath);
        var outputPath = Path.Combine(temp.Path, "trimmed.mp4");
        var extracted = new List<(string Path, TimeSpan Start, TimeSpan End)>();
        var service = CreateService(
            (_, _, _) => throw new InvalidOperationException("Full extractor should not run."),
            (_, arguments, _, _, _, _) =>
            {
                File.WriteAllText(outputPath, "trimmed");
                Assert.True(File.Exists(GetOptionValue(arguments, "-i")));
                return Task.FromResult(new ProcessRunResult(0, false, false, "", ""));
            },
            (path, start, end, _, tempH264Path, _) =>
            {
                extracted.Add((Path.GetFileName(path), start, end));
                File.WriteAllText(tempH264Path, Path.GetFileNameWithoutExtension(path));
                return CreateWindowResult(tempH264Path, start, keyframe: start);
            });

        var result = await service.RemuxTrimmedSplitAsync(
            plan.Segments[0].FilePath,
            outputPath,
            OutputFormat.Mp4,
            FpsOption.FromLabel("30"),
            plan,
            timeline,
            new TrimRange(TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(12)),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.StandardError);
        Assert.Equal(
            [
                ("dvrfile00000001.dat", TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(10)),
                ("dvrfile00000002.dat", TimeSpan.Zero, TimeSpan.FromSeconds(2))
            ],
            extracted);
    }

    [Fact]
    public async Task RemuxTrimmedAsync_TrimExtractionFailure_DoesNotFallbackToFullOutput()
    {
        using var temp = new TempDirectory();
        var inputPath = Path.Combine(temp.Path, "clip.dat");
        var outputPath = Path.Combine(temp.Path, "clip.mp4");
        File.WriteAllText(inputPath, "source");
        var ffmpegCalled = false;
        var service = CreateService(
            (_, _, _) => throw new InvalidOperationException("Full extractor should not run."),
            (_, _, _, _, _, _) =>
            {
                ffmpegCalled = true;
                return Task.FromResult(new ProcessRunResult(0, false, false, "", ""));
            },
            (_, start, _, _, tempH264Path, _) => new DatPreviewWindowResult
            {
                Succeeded = false,
                SourcePath = inputPath,
                RequestedLocalOffset = start,
                FailureReason = "no keyframe",
                TechnicalDetails = "no keyframe"
            });

        var result = await service.RemuxTrimmedAsync(inputPath, outputPath, OutputFormat.Mp4, FpsOption.FromLabel("30"), TimeSpan.FromSeconds(10), new TrimRange(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5)), null, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.False(ffmpegCalled);
        Assert.False(File.Exists(outputPath));
        Assert.Contains("Trim Video could not be applied", result.UserMessage);
    }

    [Fact]
    public async Task RemuxTrimmedAsync_CancellationDuringExtraction_CleansTempFile()
    {
        using var temp = new TempDirectory();
        var inputPath = Path.Combine(temp.Path, "clip.dat");
        var outputPath = Path.Combine(temp.Path, "clip.mp4");
        File.WriteAllText(inputPath, "source");
        string? tempTrimPath = null;
        var service = CreateService(
            (_, _, _) => throw new InvalidOperationException("Full extractor should not run."),
            (_, _, _, _, _, _) => throw new InvalidOperationException("FFmpeg should not run."),
            (_, _, _, _, tempH264Path, _) =>
            {
                tempTrimPath = tempH264Path;
                File.WriteAllText(tempH264Path, "partial");
                throw new OperationCanceledException();
            });

        var result = await service.RemuxTrimmedAsync(inputPath, outputPath, OutputFormat.Mp4, FpsOption.FromLabel("30"), TimeSpan.FromSeconds(10), new TrimRange(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5)), null, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.True(result.WasCanceled);
        Assert.NotNull(tempTrimPath);
        Assert.False(File.Exists(tempTrimPath!));
    }

    [Fact]
    public async Task EncodeTrimmedAsync_UsesCleanTempH264InputWithPreRollAndDuration()
    {
        using var temp = new TempDirectory();
        var inputPath = Path.Combine(temp.Path, "clip.dat");
        var outputPath = Path.Combine(temp.Path, "clip.mp4");
        File.WriteAllText(inputPath, "source");
        string? tempInputPath = null;
        string? tempTrimPath = null;
        var service = CreateService(
            (_, _, _) => throw new InvalidOperationException("Full extractor should not run."),
            (_, arguments, _, _, _, _) =>
            {
                tempInputPath = GetOptionValue(arguments, "-i");
                Assert.Equal("1.5", GetOptionValue(arguments, "-ss"));
                Assert.Equal("3", GetOptionValue(arguments, "-t"));
                Assert.Contains("-movflags", arguments);
                Assert.Contains("+faststart", arguments);
                File.WriteAllText(outputPath, "encoded trim");
                return Task.FromResult(new ProcessRunResult(0, false, false, "", ""));
            },
            (_, start, end, _, tempH264Path, _) =>
            {
                tempTrimPath = tempH264Path;
                Assert.Equal(TimeSpan.FromSeconds(2), start);
                Assert.Equal(TimeSpan.FromSeconds(5), end);
                File.WriteAllText(tempH264Path, "trimmed h264");
                return CreateWindowResult(tempH264Path, start, keyframe: TimeSpan.FromSeconds(0.5));
            });

        var result = await service.EncodeTrimmedAsync(inputPath, outputPath, OutputFormat.Mp4, FpsOption.FromLabel("30"), TimeSpan.FromSeconds(10), new TrimRange(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5)), null, CancellationToken.None);

        Assert.True(result.IsSuccess, result.StandardError);
        Assert.Equal("Encode", result.ConversionMode);
        Assert.Equal(ConversionInputPathMode.TrimmedCleanH264, result.InputPathMode);
        Assert.Equal(tempTrimPath, tempInputPath);
        Assert.NotNull(tempTrimPath);
        Assert.False(File.Exists(tempTrimPath!));
    }

    [Fact]
    public async Task EncodeTrimmedAsync_ReportsPreparationAndUsesTrimDurationForProgress()
    {
        using var temp = new TempDirectory();
        var inputPath = Path.Combine(temp.Path, "clip.dat");
        var outputPath = Path.Combine(temp.Path, "clip.mp4");
        File.WriteAllText(inputPath, "source");
        var progressUpdates = new List<ConversionProgress>();
        var service = CreateService(
            (_, _, _) => throw new InvalidOperationException("Full clean extractor should not run."),
            (_, _, _, _, onOutput, _) =>
            {
                onOutput?.Invoke("out_time_us=1500000");
                onOutput?.Invoke("progress=continue");
                File.WriteAllText(outputPath, "encoded trim");
                return Task.FromResult(new ProcessRunResult(0, false, false, "", ""));
            },
            (_, start, _, _, tempH264Path, _) =>
            {
                File.WriteAllText(tempH264Path, "trimmed h264");
                return CreateWindowResult(tempH264Path, start, keyframe: start);
            });
        var progress = new CapturingProgress(progressUpdates);

        var result = await service.EncodeTrimmedAsync(inputPath, outputPath, OutputFormat.Mp4, FpsOption.FromLabel("30"), TimeSpan.FromSeconds(10), new TrimRange(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5)), progress, CancellationToken.None);

        Assert.True(result.IsSuccess, result.StandardError);
        Assert.Contains(progressUpdates, update => update.Summary == "Preparing selected trim...");
        Assert.Contains(progressUpdates, update => update.Summary == "Encoding selected trim...");
        Assert.Contains(progressUpdates, update => update.Percent == 50);
    }

    [Fact]
    public async Task EncodeTrimmedAsync_UsesProvidedOutputPathAsFinalFfmpegTarget()
    {
        using var temp = new TempDirectory();
        var sourceFolder = Path.Combine(temp.Path, "source");
        var outputFolder = Path.Combine(temp.Path, "chosen-output");
        Directory.CreateDirectory(sourceFolder);
        Directory.CreateDirectory(outputFolder);
        var inputPath = Path.Combine(sourceFolder, "clip.dat");
        var outputPath = Path.Combine(outputFolder, "clip_trim_000002-000005.mp4");
        var wrongSourceOutputPath = Path.Combine(sourceFolder, Path.GetFileName(outputPath));
        File.WriteAllText(inputPath, "source");
        var service = CreateService(
            (_, _, _) => throw new InvalidOperationException("Full extractor should not run."),
            (_, arguments, _, _, _, _) =>
            {
                Assert.Equal(outputPath, arguments[^1]);
                File.WriteAllText(arguments[^1], "encoded trim");
                return Task.FromResult(new ProcessRunResult(0, false, false, "", ""));
            },
            (_, start, _, _, tempH264Path, _) =>
            {
                File.WriteAllText(tempH264Path, "trimmed h264");
                return CreateWindowResult(tempH264Path, start, keyframe: start);
            });

        var result = await service.EncodeTrimmedAsync(inputPath, outputPath, OutputFormat.Mp4, FpsOption.FromLabel("30"), TimeSpan.FromSeconds(10), new TrimRange(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5)), null, CancellationToken.None);

        Assert.True(result.IsSuccess, result.StandardError);
        Assert.Equal(outputPath, result.OutputPath);
        Assert.True(File.Exists(outputPath));
        Assert.False(File.Exists(wrongSourceOutputPath));
    }

    [Fact]
    public async Task EncodeTrimmedAsync_MkvOmitsMp4OnlyMovflagsAndUsesFractionalFps()
    {
        using var temp = new TempDirectory();
        var inputPath = Path.Combine(temp.Path, "clip.dat");
        var outputPath = Path.Combine(temp.Path, "clip.mkv");
        File.WriteAllText(inputPath, "source");
        var service = CreateService(
            (_, _, _) => throw new InvalidOperationException("Full extractor should not run."),
            (_, arguments, _, _, _, _) =>
            {
                Assert.Equal("30000/1001", GetOptionValue(arguments, "-r"));
                Assert.Contains("setpts=PTS-STARTPTS,fps=30000/1001,format=yuv420p", arguments);
                Assert.DoesNotContain("-movflags", arguments);
                File.WriteAllText(outputPath, "encoded trim");
                return Task.FromResult(new ProcessRunResult(0, false, false, "", ""));
            },
            (_, start, _, _, tempH264Path, _) =>
            {
                File.WriteAllText(tempH264Path, "trimmed h264");
                return CreateWindowResult(tempH264Path, start, keyframe: start);
            });

        var result = await service.EncodeTrimmedAsync(inputPath, outputPath, OutputFormat.Mkv, FpsOption.FromLabel("29.97"), TimeSpan.FromSeconds(10), new TrimRange(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5)), null, CancellationToken.None);

        Assert.True(result.IsSuccess, result.StandardError);
    }

    [Fact]
    public async Task EncodeTrimmedNvencAsync_UsesNvencSettingsAndPtsStartPts()
    {
        using var temp = new TempDirectory();
        var inputPath = Path.Combine(temp.Path, "clip.dat");
        var outputPath = Path.Combine(temp.Path, "clip.mp4");
        File.WriteAllText(inputPath, "source");
        var service = CreateService(
            (_, _, _) => throw new InvalidOperationException("Full extractor should not run."),
            (_, arguments, _, _, _, _) =>
            {
                Assert.Equal("1.5", GetOptionValue(arguments, "-ss"));
                Assert.Equal("3", GetOptionValue(arguments, "-t"));
                Assert.Equal("setpts=PTS-STARTPTS,fps=30,format=yuv420p", GetOptionValue(arguments, "-vf"));
                Assert.Equal("h264_nvenc", GetOptionValue(arguments, "-c:v"));
                Assert.Equal("p1", GetOptionValue(arguments, "-preset"));
                Assert.Equal("23", GetOptionValue(arguments, "-cq"));
                Assert.Equal("0", GetOptionValue(arguments, "-b:v"));
                File.WriteAllText(outputPath, "encoded trim");
                return Task.FromResult(new ProcessRunResult(0, false, false, "", ""));
            },
            (_, start, end, _, tempH264Path, _) =>
            {
                Assert.Equal(TimeSpan.FromSeconds(2), start);
                Assert.Equal(TimeSpan.FromSeconds(5), end);
                File.WriteAllText(tempH264Path, "trimmed h264");
                return CreateWindowResult(tempH264Path, start, keyframe: TimeSpan.FromSeconds(0.5));
            });

        var result = await service.EncodeTrimmedNvencAsync(inputPath, outputPath, OutputFormat.Mp4, FpsOption.FromLabel("30"), TimeSpan.FromSeconds(10), new TrimRange(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5)), null, CancellationToken.None);

        Assert.True(result.IsSuccess, result.StandardError);
        Assert.Equal(ConversionModes.EncodeNvenc, result.ConversionMode);
        Assert.Equal(ConversionInputPathMode.TrimmedCleanH264, result.InputPathMode);
    }

    [Fact]
    public async Task EncodeTrimmedSplitAsync_CrossingBoundary_ExtractsRelevantSegmentsAndUsesFirstPreRoll()
    {
        using var temp = new TempDirectory();
        var plan = CreateSplitPlan(temp.Path);
        var timeline = RecordingTimelineBuilder.FromSplitExportPlan(plan, plan.Segments[0].FilePath);
        var outputPath = Path.Combine(temp.Path, "trimmed.mp4");
        var extracted = new List<(string Path, TimeSpan Start, TimeSpan End)>();
        var service = CreateService(
            (_, _, _) => throw new InvalidOperationException("Full extractor should not run."),
            (_, arguments, _, _, _, _) =>
            {
                Assert.Equal("1", GetOptionValue(arguments, "-ss"));
                Assert.Equal("4", GetOptionValue(arguments, "-t"));
                File.WriteAllText(outputPath, "encoded trim");
                return Task.FromResult(new ProcessRunResult(0, false, false, "", ""));
            },
            (path, start, end, _, tempH264Path, _) =>
            {
                extracted.Add((Path.GetFileName(path), start, end));
                File.WriteAllText(tempH264Path, Path.GetFileNameWithoutExtension(path));
                var keyframe = start == TimeSpan.FromSeconds(8) ? TimeSpan.FromSeconds(7) : start;
                return CreateWindowResult(tempH264Path, start, keyframe);
            });

        var result = await service.EncodeTrimmedSplitAsync(
            plan.Segments[0].FilePath,
            outputPath,
            OutputFormat.Mp4,
            FpsOption.FromLabel("30"),
            plan,
            timeline,
            new TrimRange(TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(12)),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.StandardError);
        Assert.Equal(
            [
                ("dvrfile00000001.dat", TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(10)),
                ("dvrfile00000002.dat", TimeSpan.Zero, TimeSpan.FromSeconds(2))
            ],
            extracted);
    }

    [Fact]
    public async Task EncodeTrimmedSplitAsync_UsesProvidedOutputPathAsFinalFfmpegTarget()
    {
        using var temp = new TempDirectory();
        var sourceFolder = Path.Combine(temp.Path, "split-source");
        var outputFolder = Path.Combine(temp.Path, "chosen-output");
        Directory.CreateDirectory(sourceFolder);
        Directory.CreateDirectory(outputFolder);
        var plan = CreateSplitPlan(sourceFolder);
        var timeline = RecordingTimelineBuilder.FromSplitExportPlan(plan, plan.Segments[0].FilePath);
        var outputPath = Path.Combine(outputFolder, "Cam 8379 - 4 hr clip_trim_260522_0440-260522_0458.mkv");
        var wrongSourceOutputPath = Path.Combine(sourceFolder, Path.GetFileName(outputPath));
        var service = CreateService(
            (_, _, _) => throw new InvalidOperationException("Full extractor should not run."),
            (_, arguments, _, _, _, _) =>
            {
                Assert.Equal(outputPath, arguments[^1]);
                File.WriteAllText(arguments[^1], "encoded trim");
                return Task.FromResult(new ProcessRunResult(0, false, false, "", ""));
            },
            (_, start, _, _, tempH264Path, _) =>
            {
                File.WriteAllText(tempH264Path, "trimmed h264");
                return CreateWindowResult(tempH264Path, start, keyframe: start);
            });

        var result = await service.EncodeTrimmedSplitAsync(
            plan.Segments[0].FilePath,
            outputPath,
            OutputFormat.Mkv,
            FpsOption.FromLabel("30"),
            plan,
            timeline,
            new TrimRange(TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(12)),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.StandardError);
        Assert.Equal(outputPath, result.OutputPath);
        Assert.True(File.Exists(outputPath));
        Assert.False(File.Exists(wrongSourceOutputPath));
    }

    [Fact]
    public async Task EncodeTrimmedAsync_TrimExtractionFailure_DoesNotFallbackToFullOutput()
    {
        using var temp = new TempDirectory();
        var inputPath = Path.Combine(temp.Path, "clip.dat");
        var outputPath = Path.Combine(temp.Path, "clip.mp4");
        File.WriteAllText(inputPath, "source");
        var ffmpegCalled = false;
        var service = CreateService(
            (_, _, _) => throw new InvalidOperationException("Full extractor should not run."),
            (_, _, _, _, _, _) =>
            {
                ffmpegCalled = true;
                return Task.FromResult(new ProcessRunResult(0, false, false, "", ""));
            },
            (_, start, _, _, _, _) => new DatPreviewWindowResult
            {
                Succeeded = false,
                SourcePath = inputPath,
                RequestedLocalOffset = start,
                FailureReason = "no keyframe",
                TechnicalDetails = "no keyframe"
            });

        var result = await service.EncodeTrimmedAsync(inputPath, outputPath, OutputFormat.Mp4, FpsOption.FromLabel("30"), TimeSpan.FromSeconds(10), new TrimRange(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5)), null, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.False(ffmpegCalled);
        Assert.False(File.Exists(outputPath));
        Assert.Contains("Trim Video could not be applied", result.UserMessage);
    }

    [Fact]
    public async Task EncodeTrimmedAsync_CancellationDuringExtraction_CleansTempFile()
    {
        using var temp = new TempDirectory();
        var inputPath = Path.Combine(temp.Path, "clip.dat");
        var outputPath = Path.Combine(temp.Path, "clip.mp4");
        File.WriteAllText(inputPath, "source");
        string? tempTrimPath = null;
        var service = CreateService(
            (_, _, _) => throw new InvalidOperationException("Full extractor should not run."),
            (_, _, _, _, _, _) => throw new InvalidOperationException("FFmpeg should not run."),
            (_, _, _, _, tempH264Path, _) =>
            {
                tempTrimPath = tempH264Path;
                File.WriteAllText(tempH264Path, "partial");
                throw new OperationCanceledException();
            });

        var result = await service.EncodeTrimmedAsync(inputPath, outputPath, OutputFormat.Mp4, FpsOption.FromLabel("30"), TimeSpan.FromSeconds(10), new TrimRange(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5)), null, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.True(result.WasCanceled);
        Assert.NotNull(tempTrimPath);
        Assert.False(File.Exists(tempTrimPath!));
    }

    [Fact]
    public void TrimConversionPolicy_FullEncodeWithTrim_IsSupported()
    {
        var trim = new TrimRange(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));

        Assert.False(TrimConversionPolicy.ShouldBlockTrimmedConversion(trim, "Encode"));
        Assert.True(TrimConversionPolicy.IsTrimSupportedForConversionMode("Encode"));
    }

    [Fact]
    public void TrimConversionPolicy_FullNvencWithTrim_IsSupported()
    {
        var trim = new TrimRange(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));

        Assert.False(TrimConversionPolicy.ShouldBlockTrimmedConversion(trim, ConversionModes.EncodeNvenc));
        Assert.False(TrimConversionPolicy.ShouldBlockTrimmedConversion(trim, ConversionModes.FullNvencDisplayName));
        Assert.True(TrimConversionPolicy.IsTrimSupportedForConversionMode(ConversionModes.EncodeNvenc));
    }

    [Fact]
    public void TrimConversionPolicy_FastRemuxWithTrim_IsSupported()
    {
        var trim = new TrimRange(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));

        Assert.False(TrimConversionPolicy.ShouldBlockTrimmedConversion(trim, "Remux"));
        Assert.True(TrimConversionPolicy.IsTrimSupportedForConversionMode("Remux"));
        Assert.Equal("Remux", TrimConversionPolicy.ResolveModeForTrim(trim, "Remux"));
    }

    [Fact]
    public void TrimConversionPolicy_TrimSetWhileFullSelected_KeepsFull()
    {
        var trim = new TrimRange(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));

        var mode = TrimConversionPolicy.ResolveModeForTrim(trim, "Encode");

        Assert.Equal("Encode", mode);
    }

    [Fact]
    public void TrimConversionPolicy_FullIsSelectableAndAcceptedWhileTrimIsSet()
    {
        var trim = new TrimRange(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));

        Assert.True(TrimConversionPolicy.CanSelectMode(trim));
        Assert.Equal("Encode", TrimConversionPolicy.ResolveModeForTrim(trim, "Encode"));
    }

    [Fact]
    public void TrimConversionPolicy_ClearingTrimRestoresNormalModeChoices()
    {
        Assert.True(TrimConversionPolicy.CanSelectMode(null));
        Assert.Equal("Encode", TrimConversionPolicy.ResolveModeForTrim(null, "Encode"));
    }

    [Fact]
    public void TrimConversionPolicy_NoTrimFullModeRemainsAvailable()
    {
        Assert.False(TrimConversionPolicy.ShouldBlockTrimmedConversion(null, "Encode"));
        Assert.True(TrimConversionPolicy.IsTrimSupportedForConversionMode("Fast"));
    }

    private static ConversionService CreateService(
        Func<string, string, CancellationToken, SpotterDatPayloadExtractionResult> extract,
        Func<string, IReadOnlyList<string>, TimeSpan, CancellationToken, Action<string>?, Action<string>?, Task<ProcessRunResult>> runProcessAsync,
        Func<string, TimeSpan, TimeSpan, TimeSpan?, string, CancellationToken, DatPreviewWindowResult> extractTrimmed)
    {
        return new ConversionService(
            new FfmpegTools(AppContext.BaseDirectory, "ffmpeg.exe", "ffprobe.exe", FfmpegExists: true, FfprobeExists: true),
            InternalConversionPathOptions.Default,
            extract,
            extractTrimmed,
            runProcessAsync);
    }

    private static SpotterSplitExportPlan CreateSplitPlan(string folder)
    {
        var start = new DateTime(2026, 5, 27, 10, 0, 0);
        var segments = new List<SpotterSplitExportSegment>();
        for (var index = 0; index < 2; index++)
        {
            var path = Path.Combine(folder, $"dvrfile{index + 1:00000000}.dat");
            File.WriteAllText(path, "source");
            segments.Add(new SpotterSplitExportSegment
            {
                SegmentNumber = index + 1,
                FileName = Path.GetFileName(path),
                FilePath = path,
                StartTime = start.AddSeconds(index * 10),
                EndTime = start.AddSeconds(index * 10 + 10)
            });
        }

        return new SpotterSplitExportPlan
        {
            ExportFolder = folder,
            Confidence = "Strong",
            SelectedSegmentNumber = 1,
            SelectedSourcePath = segments[0].FilePath,
            Segments = segments
        };
    }

    private static DatPreviewWindowResult CreateWindowResult(string outputPath, TimeSpan requested, TimeSpan keyframe)
    {
        return new DatPreviewWindowResult
        {
            Succeeded = true,
            OutputPath = outputPath,
            SourcePath = Path.ChangeExtension(outputPath, ".dat"),
            RequestedLocalOffset = requested,
            SelectedKeyframeLocalOffset = keyframe,
            PreviewSeekOffset = requested - keyframe,
            WrittenFrameRecordCount = 1,
            WrittenPayloadBytes = 12,
            TechnicalDetails = $"Selected keyframe local offset: {keyframe}"
        };
    }

    private sealed class CapturingProgress : IProgress<ConversionProgress>
    {
        private readonly List<ConversionProgress> updates;

        public CapturingProgress(List<ConversionProgress> updates)
        {
            this.updates = updates;
        }

        public void Report(ConversionProgress value)
        {
            updates.Add(value);
        }
    }

    private static SpotterDatPayloadExtractionResult CreateExtractionResult(string inputPath, string outputPath)
    {
        return new SpotterDatPayloadExtractionResult
        {
            Succeeded = true,
            InputPath = inputPath,
            OutputPath = outputPath,
            InputFileSize = 100,
            ExtractedPayloadByteCount = 80,
            FrameRecordCount = 1,
            ExtractedFrameRecordCount = 1,
            CandidateNalUnitCount = 3,
            SpsCount = 1,
            PpsCount = 1,
            IdrFrameCount = 1,
            LookedConfident = true
        };
    }

    private static string GetOptionValue(IReadOnlyList<string> arguments, string option)
    {
        var index = arguments.ToList().IndexOf(option);
        Assert.True(index >= 0 && index < arguments.Count - 1, $"Missing option value for {option}.");
        return arguments[index + 1];
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
