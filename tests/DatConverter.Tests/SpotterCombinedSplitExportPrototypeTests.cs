namespace DatConverter.Tests;

public sealed class SpotterCombinedSplitExportPrototypeTests
{
    [Fact]
    public async Task RunAsync_StrongSplitPlanCombinesSegmentsInPlanOrder()
    {
        using var temp = new TempDirectory();
        var plan = CreatePlan(temp.Path, segmentCount: 3);
        var outputPath = Path.Combine(temp.Path, "combined.mp4");
        var ffmpegPath = CreateFfmpeg(temp.Path);
        var extractionOrder = new List<string>();
        string? combinedInput = null;
        var prototype = new SpotterCombinedSplitExportPrototype(
            _ => plan,
            (inputPath, tempH264Path, _) =>
            {
                extractionOrder.Add(Path.GetFileName(inputPath));
                File.WriteAllText(tempH264Path, Path.GetFileNameWithoutExtension(inputPath) + ";");
                return CreateExtractionResult(inputPath, tempH264Path, confident: true);
            },
            (_, arguments, _, _) =>
            {
                combinedInput = GetOptionValue(arguments, "-i");
                Assert.Equal("dvrfile00000001;dvrfile00000002;dvrfile00000003;", File.ReadAllText(combinedInput));
                File.WriteAllText(outputPath, "mp4");
                return Task.FromResult(new ProcessRunResult(0, false, false, "ok", ""));
            });

        var result = await prototype.RunAsync(plan.Segments[1].FilePath, outputPath, ffmpegPath, FpsOption.FromLabel("30"), keepTemp: false, CancellationToken.None);

        Assert.True(result.Succeeded, result.FailureReason);
        Assert.Equal(new[] { "dvrfile00000001.dat", "dvrfile00000002.dat", "dvrfile00000003.dat" }, extractionOrder);
        Assert.NotNull(combinedInput);
        Assert.False(File.Exists(combinedInput!));
        Assert.False(Directory.EnumerateFiles(temp.Path, "*.h264").Any());
        Assert.Contains("Raw H.264 concatenation", result.Warnings.Single());
    }

    [Fact]
    public async Task RunAsync_SelectedDatPathAndFolderPathCanResolveSamePlan()
    {
        using var temp = new TempDirectory();
        var plan = CreatePlan(temp.Path, segmentCount: 2);
        var ffmpegPath = CreateFfmpeg(temp.Path);
        var inputPaths = new List<string>();
        var prototype = new SpotterCombinedSplitExportPrototype(
            inputPath =>
            {
                inputPaths.Add(inputPath);
                return plan;
            },
            (inputPath, tempH264Path, _) =>
            {
                File.WriteAllText(tempH264Path, Path.GetFileName(inputPath));
                return CreateExtractionResult(inputPath, tempH264Path, confident: true);
            },
            (_, arguments, _, _) =>
            {
                File.WriteAllText(arguments[^1], "out");
                return Task.FromResult(new ProcessRunResult(0, false, false, "", ""));
            });

        var selectedResult = await prototype.RunAsync(plan.Segments[0].FilePath, Path.Combine(temp.Path, "selected.mp4"), ffmpegPath, FpsOption.FromLabel("30"), false, CancellationToken.None);
        var folderResult = await prototype.RunAsync(temp.Path, Path.Combine(temp.Path, "folder.mp4"), ffmpegPath, FpsOption.FromLabel("30"), false, CancellationToken.None);

        Assert.True(selectedResult.Succeeded, selectedResult.FailureReason);
        Assert.True(folderResult.Succeeded, folderResult.FailureReason);
        Assert.Equal(new[] { plan.Segments[0].FilePath, temp.Path }, inputPaths);
    }

    [Fact]
    public async Task RunAsync_WeakPlanRefusesBeforeExtraction()
    {
        using var temp = new TempDirectory();
        var outputPath = Path.Combine(temp.Path, "combined.mp4");
        var ffmpegPath = CreateFfmpeg(temp.Path);
        var extractionCalled = false;
        var prototype = new SpotterCombinedSplitExportPrototype(
            _ => CreatePlan(temp.Path, segmentCount: 2, confidence: "Weak"),
            (_, _, _) =>
            {
                extractionCalled = true;
                throw new InvalidOperationException("Extraction should not run.");
            },
            (_, _, _, _) => throw new InvalidOperationException("FFmpeg should not run."));

        var result = await prototype.RunAsync(temp.Path, outputPath, ffmpegPath, FpsOption.FromLabel("30"), false, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.False(extractionCalled);
        Assert.Contains("not strong", result.FailureReason);
    }

    [Fact]
    public async Task RunAsync_MissingSegmentRefusesCombinedConversion()
    {
        using var temp = new TempDirectory();
        var plan = CreatePlan(temp.Path, segmentCount: 2);
        File.Delete(plan.Segments[1].FilePath);
        var prototype = new SpotterCombinedSplitExportPrototype(
            _ => plan,
            (_, _, _) => throw new InvalidOperationException("Extraction should not run."),
            (_, _, _, _) => throw new InvalidOperationException("FFmpeg should not run."));

        var result = await prototype.RunAsync(temp.Path, Path.Combine(temp.Path, "combined.mp4"), CreateFfmpeg(temp.Path), FpsOption.FromLabel("30"), false, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("not found", result.FailureReason);
    }

    [Fact]
    public async Task RunAsync_ExistingOutputIsRefused()
    {
        using var temp = new TempDirectory();
        var outputPath = Path.Combine(temp.Path, "combined.mp4");
        File.WriteAllText(outputPath, "existing");
        var prototype = new SpotterCombinedSplitExportPrototype(
            _ => throw new InvalidOperationException("Plan should not run."),
            (_, _, _) => throw new InvalidOperationException("Extraction should not run."),
            (_, _, _, _) => throw new InvalidOperationException("FFmpeg should not run."));

        var result = await prototype.RunAsync(temp.Path, outputPath, CreateFfmpeg(temp.Path), FpsOption.FromLabel("30"), false, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("already exists", result.FailureReason);
        Assert.Equal("existing", File.ReadAllText(outputPath));
    }

    [Fact]
    public async Task RunAsync_UnsupportedOutputExtensionIsRefused()
    {
        using var temp = new TempDirectory();
        var prototype = new SpotterCombinedSplitExportPrototype(
            _ => throw new InvalidOperationException("Plan should not run."),
            (_, _, _) => throw new InvalidOperationException("Extraction should not run."),
            (_, _, _, _) => throw new InvalidOperationException("FFmpeg should not run."));

        var result = await prototype.RunAsync(temp.Path, Path.Combine(temp.Path, "combined.avi"), CreateFfmpeg(temp.Path), FpsOption.FromLabel("30"), false, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains(".mp4 or .mkv", result.FailureReason);
    }

    [Fact]
    public async Task RunAsync_FailedSegmentExtractionStopsAndCleansTempFiles()
    {
        using var temp = new TempDirectory();
        var plan = CreatePlan(temp.Path, segmentCount: 2);
        var prototype = new SpotterCombinedSplitExportPrototype(
            _ => plan,
            (inputPath, tempH264Path, _) =>
            {
                File.WriteAllText(tempH264Path, "partial");
                var confident = inputPath.EndsWith("00000001.dat", StringComparison.OrdinalIgnoreCase);
                return CreateExtractionResult(inputPath, tempH264Path, confident);
            },
            (_, _, _, _) => throw new InvalidOperationException("FFmpeg should not run."));

        var result = await prototype.RunAsync(temp.Path, Path.Combine(temp.Path, "combined.mp4"), CreateFfmpeg(temp.Path), FpsOption.FromLabel("30"), false, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("not confident", result.FailureReason);
        Assert.False(Directory.EnumerateFiles(temp.Path, "*.h264").Any());
    }

    [Fact]
    public async Task RunAsync_FfmpegFailureReportsDetailsAndCleansTempFiles()
    {
        using var temp = new TempDirectory();
        var plan = CreatePlan(temp.Path, segmentCount: 2);
        var outputPath = Path.Combine(temp.Path, "combined.mp4");
        var prototype = new SpotterCombinedSplitExportPrototype(
            _ => plan,
            (inputPath, tempH264Path, _) =>
            {
                File.WriteAllText(tempH264Path, "clean");
                return CreateExtractionResult(inputPath, tempH264Path, confident: true);
            },
            (_, _, _, _) =>
            {
                File.WriteAllText(outputPath, "partial");
                return Task.FromResult(new ProcessRunResult(1, false, false, "", "bad stream"));
            });

        var result = await prototype.RunAsync(temp.Path, outputPath, CreateFfmpeg(temp.Path), FpsOption.FromLabel("30"), false, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("FFmpeg", result.FailureReason);
        Assert.Contains("bad stream", result.BuildTechnicalReport());
        Assert.False(File.Exists(outputPath));
        Assert.False(Directory.EnumerateFiles(temp.Path, "*.h264").Any());
    }

    [Fact]
    public async Task RunAsync_CancellationDuringExtractionCleansTempFiles()
    {
        using var temp = new TempDirectory();
        var plan = CreatePlan(temp.Path, segmentCount: 2);
        var prototype = new SpotterCombinedSplitExportPrototype(
            _ => plan,
            (_, tempH264Path, _) =>
            {
                File.WriteAllText(tempH264Path, "partial");
                throw new OperationCanceledException();
            },
            (_, _, _, _) => throw new InvalidOperationException("FFmpeg should not run."));

        var result = await prototype.RunAsync(temp.Path, Path.Combine(temp.Path, "combined.mp4"), CreateFfmpeg(temp.Path), FpsOption.FromLabel("30"), false, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("canceled", result.FailureReason);
        Assert.False(Directory.EnumerateFiles(temp.Path, "*.h264").Any());
    }

    [Fact]
    public async Task RunAsync_FpsArgumentFlowsIntoRemuxCommand()
    {
        using var temp = new TempDirectory();
        var plan = CreatePlan(temp.Path, segmentCount: 2);
        string? fpsValue = null;
        var prototype = new SpotterCombinedSplitExportPrototype(
            _ => plan,
            (inputPath, tempH264Path, _) =>
            {
                File.WriteAllText(tempH264Path, "clean");
                return CreateExtractionResult(inputPath, tempH264Path, confident: true);
            },
            (_, arguments, _, _) =>
            {
                fpsValue = GetOptionValue(arguments, "-r");
                File.WriteAllText(arguments[^1], "mp4");
                return Task.FromResult(new ProcessRunResult(0, false, false, "", ""));
            });

        var result = await prototype.RunAsync(temp.Path, Path.Combine(temp.Path, "combined.mp4"), CreateFfmpeg(temp.Path), FpsOption.FromLabel("29.97"), false, CancellationToken.None);

        Assert.True(result.Succeeded, result.FailureReason);
        Assert.Equal("30000/1001", fpsValue);
        Assert.Equal("30000/1001", result.FpsValue);
    }

    private static SpotterSplitExportPlan CreatePlan(string folder, int segmentCount, string confidence = "Strong")
    {
        var segments = new List<SpotterSplitExportSegment>();
        var start = new DateTime(2026, 5, 22, 3, 59, 59, 480);
        for (var index = 1; index <= segmentCount; index++)
        {
            var fileName = $"dvrfile{index:00000000}.dat";
            var filePath = Path.Combine(folder, fileName);
            File.WriteAllText(filePath, "not relevant");
            segments.Add(new SpotterSplitExportSegment
            {
                SegmentNumber = index,
                FileName = fileName,
                FilePath = filePath,
                StartTime = start.AddSeconds(index * 10),
                EndTime = start.AddSeconds(index * 10 + 9),
                GapFromPrevious = index == 1 ? null : TimeSpan.FromMilliseconds(34)
            });
        }

        return new SpotterSplitExportPlan
        {
            ExportFolder = folder,
            SelectedSourcePath = segments[0].FilePath,
            SelectedSegmentNumber = 1,
            Segments = segments,
            Confidence = confidence
        };
    }

    private static SpotterDatPayloadExtractionResult CreateExtractionResult(string inputPath, string outputPath, bool confident)
    {
        return new SpotterDatPayloadExtractionResult
        {
            Succeeded = confident,
            InputPath = inputPath,
            OutputPath = outputPath,
            InputFileSize = 100,
            ExtractedPayloadByteCount = confident ? new FileInfo(outputPath).Length : 0,
            FrameRecordCount = 1,
            ExtractedFrameRecordCount = confident ? 1 : 0,
            CandidateNalUnitCount = confident ? 3 : 0,
            SpsCount = confident ? 1 : 0,
            PpsCount = confident ? 1 : 0,
            IdrFrameCount = confident ? 1 : 0,
            LookedConfident = confident,
            FailureReason = confident ? null : "failed"
        };
    }

    private static string CreateFfmpeg(string folder)
    {
        var path = Path.Combine(folder, "ffmpeg.exe");
        File.WriteAllText(path, "");
        return path;
    }

    private static string GetOptionValue(IReadOnlyList<string> arguments, string option)
    {
        var index = arguments.ToList().IndexOf(option);
        Assert.True(index >= 0 && index < arguments.Count - 1, $"Missing option value for {option}.");
        return arguments[index + 1];
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
