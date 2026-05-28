namespace DatConverter;

public sealed class SpotterCombinedSplitExportPrototype
{
    private static readonly TimeSpan FfmpegTimeout = TimeSpan.FromHours(12);

    private readonly Func<string, SpotterSplitExportPlan> buildPlan;
    private readonly Func<string, string, CancellationToken, SpotterDatPayloadExtractionResult> extract;
    private readonly Func<string, IReadOnlyList<string>, TimeSpan, CancellationToken, Task<ProcessRunResult>> runProcessAsync;

    public SpotterCombinedSplitExportPrototype()
        : this(
            path => new SpotterSplitExportPlanBuilder().Build(path),
            (inputPath, outputPath, cancellationToken) => new SpotterDatPayloadExtractor().Extract(inputPath, outputPath, cancellationToken),
            (executablePath, arguments, timeout, cancellationToken) =>
                FfmpegProcessRunner.RunAsync(executablePath, arguments, timeout, cancellationToken))
    {
    }

    public SpotterCombinedSplitExportPrototype(
        Func<string, SpotterSplitExportPlan> buildPlan,
        Func<string, string, CancellationToken, SpotterDatPayloadExtractionResult> extract,
        Func<string, IReadOnlyList<string>, TimeSpan, CancellationToken, Task<ProcessRunResult>> runProcessAsync)
    {
        this.buildPlan = buildPlan;
        this.extract = extract;
        this.runProcessAsync = runProcessAsync;
    }

    public async Task<SpotterCombinedSplitExportPrototypeResult> RunAsync(
        string inputPath,
        string outputPath,
        string ffmpegPath,
        FpsOption fps,
        bool keepTemp,
        CancellationToken cancellationToken,
        ContainerMetadata? metadata = null)
    {
        var outputFormat = SpotterCleanRemuxPrototype.InferOutputFormat(outputPath);
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            return CreateFailure(inputPath, outputPath, fps, outputFormat ?? OutputFormat.Mp4, null, "Input must be a selected .dat file or export folder.");
        }

        if (outputFormat is null)
        {
            return CreateFailure(inputPath, outputPath, fps, OutputFormat.Mp4, null, "Output path must end with .mp4 or .mkv.");
        }

        var resolvedOutputFormat = outputFormat.Value;
        if (File.Exists(outputPath))
        {
            return CreateFailure(inputPath, outputPath, fps, resolvedOutputFormat, null, $"Output already exists: {outputPath}");
        }

        if (string.IsNullOrWhiteSpace(ffmpegPath) || !File.Exists(ffmpegPath))
        {
            return CreateFailure(inputPath, outputPath, fps, resolvedOutputFormat, null, $"Bundled FFmpeg was not found: {ffmpegPath}");
        }

        SpotterSplitExportPlan plan;
        try
        {
            plan = buildPlan(inputPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return CreateFailure(inputPath, outputPath, fps, resolvedOutputFormat, null, $"Split export plan could not be built: {ex.Message}");
        }

        if (!plan.IsStrongConfidence)
        {
            return CreateFailure(inputPath, outputPath, fps, resolvedOutputFormat, plan, "Split export plan was not strong enough for combined remux.");
        }

        if (plan.SegmentCount < 2)
        {
            return CreateFailure(inputPath, outputPath, fps, resolvedOutputFormat, plan, "At least two split-export segments are required.");
        }

        var missingSegment = plan.Segments.FirstOrDefault(segment => !File.Exists(segment.FilePath));
        if (missingSegment is not null)
        {
            return CreateFailure(inputPath, outputPath, fps, resolvedOutputFormat, plan, $"Referenced segment file was not found: {missingSegment.FilePath}");
        }

        var outputDirectory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        var tempDirectory = string.IsNullOrWhiteSpace(outputDirectory) ? Path.GetTempPath() : outputDirectory;
        var tempPrefix = $"{Path.GetFileNameWithoutExtension(outputPath)}.{Guid.NewGuid():N}.combined-split-export";
        var combinedTempPath = Path.Combine(tempDirectory, $"{tempPrefix}.h264");
        var segmentTempPaths = new List<string>();
        var extractionResults = new List<SpotterDatPayloadExtractionResult>();
        IReadOnlyList<string> arguments = Array.Empty<string>();
        ProcessRunResult? ffmpegResult = null;
        var tempCleanupSucceeded = true;

        try
        {
            using (var combinedStream = new FileStream(combinedTempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                foreach (var segment in plan.Segments)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var segmentTempPath = Path.Combine(tempDirectory, $"{tempPrefix}.segment{segment.SegmentNumber:000}.h264");
                    segmentTempPaths.Add(segmentTempPath);
                    var extractionResult = extract(segment.FilePath, segmentTempPath, cancellationToken);
                    extractionResults.Add(extractionResult);
                    if (!extractionResult.Succeeded || !extractionResult.LookedConfident || string.IsNullOrWhiteSpace(extractionResult.OutputPath))
                    {
                        TryDeleteFile(outputPath);
                        return new SpotterCombinedSplitExportPrototypeResult
                        {
                            Succeeded = false,
                            InputPath = inputPath,
                            OutputPath = outputPath,
                            OutputFormat = resolvedOutputFormat,
                            FpsValue = fps.FfmpegValue,
                            Plan = plan,
                            ExtractionResults = extractionResults,
                            CombinedTempH264Path = keepTemp && File.Exists(combinedTempPath) ? combinedTempPath : null,
                            KeptTempFile = keepTemp && File.Exists(combinedTempPath),
                            CombinedTempByteSize = GetFileLengthQuietly(combinedTempPath),
                            FailureReason = $"Segment extraction was not confident enough for combined remux: {segment.FileName}",
                            TempCleanupSucceeded = tempCleanupSucceeded
                        };
                    }

                    using var segmentStream = new FileStream(extractionResult.OutputPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    segmentStream.CopyTo(combinedStream);
                }
            }

            arguments = BuildCombinedRemuxArguments(combinedTempPath, outputPath, resolvedOutputFormat, fps, metadata);
            ffmpegResult = await runProcessAsync(ffmpegPath, arguments, FfmpegTimeout, cancellationToken);
            if (ffmpegResult.ExitCode != 0 || !File.Exists(outputPath))
            {
                TryDeleteFile(outputPath);
                return new SpotterCombinedSplitExportPrototypeResult
                {
                    Succeeded = false,
                    InputPath = inputPath,
                    OutputPath = outputPath,
                    OutputFormat = resolvedOutputFormat,
                    FpsValue = fps.FfmpegValue,
                    Plan = plan,
                    ExtractionResults = extractionResults,
                    CombinedTempH264Path = keepTemp && File.Exists(combinedTempPath) ? combinedTempPath : null,
                    KeptTempFile = keepTemp && File.Exists(combinedTempPath),
                    CombinedTempByteSize = GetFileLengthQuietly(combinedTempPath),
                    FfmpegArguments = arguments,
                    FfmpegResult = ffmpegResult,
                    FailureReason = "FFmpeg combined split-export remux failed.",
                    TempCleanupSucceeded = tempCleanupSucceeded
                };
            }

            return new SpotterCombinedSplitExportPrototypeResult
            {
                Succeeded = true,
                InputPath = inputPath,
                OutputPath = outputPath,
                OutputFormat = resolvedOutputFormat,
                FpsValue = fps.FfmpegValue,
                Plan = plan,
                ExtractionResults = extractionResults,
                CombinedTempH264Path = keepTemp ? combinedTempPath : null,
                KeptTempFile = keepTemp,
                CombinedTempByteSize = GetFileLengthQuietly(combinedTempPath),
                FfmpegArguments = arguments,
                FfmpegResult = ffmpegResult,
                Warnings = new[] { "Raw H.264 concatenation is intended only for strong-confidence contiguous split exports from one logical stream." },
                TempCleanupSucceeded = tempCleanupSucceeded
            };
        }
        catch (OperationCanceledException)
        {
            TryDeleteFile(outputPath);
            return new SpotterCombinedSplitExportPrototypeResult
            {
                Succeeded = false,
                InputPath = inputPath,
                OutputPath = outputPath,
                OutputFormat = resolvedOutputFormat,
                FpsValue = fps.FfmpegValue,
                Plan = plan,
                ExtractionResults = extractionResults,
                CombinedTempH264Path = keepTemp && File.Exists(combinedTempPath) ? combinedTempPath : null,
                KeptTempFile = keepTemp && File.Exists(combinedTempPath),
                CombinedTempByteSize = GetFileLengthQuietly(combinedTempPath),
                FfmpegArguments = arguments,
                FfmpegResult = ffmpegResult,
                FailureReason = "Combined split-export remux was canceled.",
                TempCleanupSucceeded = tempCleanupSucceeded
            };
        }
        finally
        {
            if (!keepTemp)
            {
                foreach (var segmentTempPath in segmentTempPaths)
                {
                    tempCleanupSucceeded &= TryDeleteFile(segmentTempPath);
                }

                tempCleanupSucceeded &= TryDeleteFile(combinedTempPath);
            }
        }
    }

    public static IReadOnlyList<string> BuildCombinedRemuxArguments(
        string h264InputPath,
        string outputPath,
        OutputFormat outputFormat,
        FpsOption fps,
        ContainerMetadata? metadata = null)
    {
        return SpotterCleanRemuxPrototype.BuildCleanRemuxArguments(h264InputPath, outputPath, outputFormat, fps, metadata);
    }

    private static SpotterCombinedSplitExportPrototypeResult CreateFailure(
        string inputPath,
        string outputPath,
        FpsOption fps,
        OutputFormat outputFormat,
        SpotterSplitExportPlan? plan,
        string reason)
    {
        return new SpotterCombinedSplitExportPrototypeResult
        {
            Succeeded = false,
            InputPath = inputPath,
            OutputPath = outputPath,
            FpsValue = string.IsNullOrWhiteSpace(fps.FfmpegValue) ? "30" : fps.FfmpegValue,
            OutputFormat = outputFormat,
            Plan = plan,
            FailureReason = reason
        };
    }

    private static long GetFileLengthQuietly(string path)
    {
        try
        {
            return File.Exists(path) ? new FileInfo(path).Length : 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static bool TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }
}
