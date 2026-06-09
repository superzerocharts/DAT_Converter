using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace DatConverter;

public sealed class ConversionService
{
    private static readonly Encoding FfmpegConcatListEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private static readonly TimeSpan ConversionTimeout = TimeSpan.FromHours(12);
    public const string NvencUnavailableMessage = "Full NVENC requires a supported NVIDIA GPU and FFmpeg NVENC support. Try Full mode instead.";

    private readonly FfmpegTools ffmpegTools;
    private readonly InternalConversionPathOptions internalOptions;
    private readonly Func<string, string, CancellationToken, SpotterDatPayloadExtractionResult> extractCleanH264;
    private readonly Func<string, TimeSpan, TimeSpan, TimeSpan?, string, CancellationToken, DatPreviewWindowResult> extractTrimmedH264;
    private readonly Func<string, IReadOnlyList<string>, TimeSpan, CancellationToken, Action<string>?, Action<string>?, Task<ProcessRunResult>> runProcessAsync;

    public ConversionService(FfmpegTools ffmpegTools)
        : this(
            ffmpegTools,
            InternalConversionPathOptions.FromEnvironment(),
            (inputPath, outputPath, cancellationToken) => new SpotterDatPayloadExtractor().Extract(inputPath, outputPath, cancellationToken),
            (inputPath, start, end, duration, outputPath, cancellationToken) => new DatPreviewWindowExtractor().ExtractRange(inputPath, start, end, duration, outputPath, cancellationToken),
            FfmpegProcessRunner.RunAsync)
    {
    }

    public ConversionService(
        FfmpegTools ffmpegTools,
        InternalConversionPathOptions internalOptions,
        Func<string, string, CancellationToken, SpotterDatPayloadExtractionResult> extractCleanH264,
        Func<string, TimeSpan, TimeSpan, TimeSpan?, string, CancellationToken, DatPreviewWindowResult>? extractTrimmedH264,
        Func<string, IReadOnlyList<string>, TimeSpan, CancellationToken, Action<string>?, Action<string>?, Task<ProcessRunResult>> runProcessAsync)
    {
        this.ffmpegTools = ffmpegTools;
        this.internalOptions = internalOptions;
        this.extractCleanH264 = extractCleanH264;
        this.extractTrimmedH264 = extractTrimmedH264 ?? ((inputPath, start, end, duration, outputPath, cancellationToken) => new DatPreviewWindowExtractor().ExtractRange(inputPath, start, end, duration, outputPath, cancellationToken));
        this.runProcessAsync = runProcessAsync;
    }

    public ConversionService(
        FfmpegTools ffmpegTools,
        InternalConversionPathOptions internalOptions,
        Func<string, string, CancellationToken, SpotterDatPayloadExtractionResult> extractCleanH264,
        Func<string, IReadOnlyList<string>, TimeSpan, CancellationToken, Action<string>?, Action<string>?, Task<ProcessRunResult>> runProcessAsync)
        : this(ffmpegTools, internalOptions, extractCleanH264, null, runProcessAsync)
    {
    }

    public async Task<ConversionResult> RemuxAsync(
        string inputPath,
        string outputPath,
        OutputFormat outputFormat,
        FpsOption fps,
        TimeSpan? duration,
        IProgress<ConversionProgress>? progress,
        CancellationToken cancellationToken,
        ContainerMetadata? metadata = null,
        BurnTimestampOptions? burnTimestamp = null)
    {
        if (!HasResolvedFps(fps))
        {
            return BuildUnresolvedFpsResult(inputPath, outputPath, outputFormat, fps, "Fast", duration);
        }

        if (ShouldUseCleanRemux(inputPath, outputFormat))
        {
            return await RemuxWithCleanExtractedH264Async(inputPath, outputPath, outputFormat, fps, duration, progress, cancellationToken, metadata);
        }

        var arguments = FfmpegCommandBuilder.BuildRemuxArguments(inputPath, outputPath, outputFormat, fps, metadata);
        var result = await RunConversionAsync(
            inputPath,
            outputPath,
            outputFormat,
            fps,
            arguments,
            "Fast",
            "Fast conversion completed.",
            ConversionResult.FastFailedMessage,
            duration,
            progress,
            cancellationToken,
            ConversionInputPathMode.StandardWholeDatRawH264);
        return AppendBurnTimestampFontWarning(result, burnTimestamp);
    }

    public async Task<ConversionResult> EncodeAsync(
        string inputPath,
        string outputPath,
        OutputFormat outputFormat,
        FpsOption fps,
        TimeSpan? duration,
        IProgress<ConversionProgress>? progress,
        CancellationToken cancellationToken,
        ContainerMetadata? metadata = null,
        BurnTimestampOptions? burnTimestamp = null)
    {
        if (!HasResolvedFps(fps))
        {
            return BuildUnresolvedFpsResult(inputPath, outputPath, outputFormat, fps, "Encode", duration);
        }

        var arguments = FfmpegCommandBuilder.BuildEncodeArguments(inputPath, outputPath, outputFormat, fps, metadata, burnTimestamp);
        var result = await RunConversionAsync(
            inputPath,
            outputPath,
            outputFormat,
            fps,
            arguments,
            "Encode",
            "Full conversion completed.",
            ConversionResult.FullFailedMessage,
            duration,
            progress,
            cancellationToken,
            ConversionInputPathMode.StandardWholeDatRawH264);
        return AppendBurnTimestampFontWarning(result, burnTimestamp);
    }

    public async Task<ConversionResult> EncodeNvencAsync(
        string inputPath,
        string outputPath,
        OutputFormat outputFormat,
        FpsOption fps,
        TimeSpan? duration,
        IProgress<ConversionProgress>? progress,
        CancellationToken cancellationToken,
        ContainerMetadata? metadata = null,
        BurnTimestampOptions? burnTimestamp = null)
    {
        if (!HasResolvedFps(fps))
        {
            return BuildUnresolvedFpsResult(inputPath, outputPath, outputFormat, fps, ConversionModes.EncodeNvenc, duration);
        }

        var arguments = FfmpegCommandBuilder.BuildNvencEncodeArguments(inputPath, outputPath, outputFormat, fps, metadata, burnTimestamp);
        var result = await RunConversionAsync(
            inputPath,
            outputPath,
            outputFormat,
            fps,
            arguments,
            ConversionModes.EncodeNvenc,
            "Full NVENC conversion completed.",
            NvencUnavailableMessage,
            duration,
            progress,
            cancellationToken,
            ConversionInputPathMode.StandardWholeDatRawH264);
        return AppendBurnTimestampFontWarning(result, burnTimestamp);
    }

    public async Task<ConversionResult> EncodeCombinedStoryboardAsync(
        SpotterStoryboardPlan plan,
        string outputPath,
        OutputFormat outputFormat,
        FpsOption fps,
        TimeSpan? duration,
        TrimRange? trimRange,
        IProgress<ConversionProgress>? progress,
        CancellationToken cancellationToken,
        ContainerMetadata? metadata = null)
    {
        var inputPath = string.IsNullOrWhiteSpace(plan.SidecarPath) ? plan.ExportFolder : plan.SidecarPath;
        if (!HasResolvedFps(fps))
        {
            return BuildUnresolvedFpsResult(inputPath, outputPath, outputFormat, fps, ConversionModes.Encode, duration);
        }

        if (!plan.IsStrongConfidence)
        {
            return new ConversionResult(
                false,
                "Storyboard export could not be verified.",
                ffmpegTools.FfmpegPath,
                Array.Empty<string>(),
                inputPath,
                outputPath,
                fps,
                null,
                "",
                "Storyboard plan was not strong enough at conversion time.",
                ConversionMode: ConversionModes.Encode,
                OutputFormat: outputFormat.DisplayName(),
                Duration: duration,
                UsedDeterminateProgress: duration.HasValue,
                InputPathMode: ConversionInputPathMode.StoryboardCombinedCleanH264);
        }

        var guardResult = BuildOutputGuardResult(inputPath, outputPath, outputFormat, fps, Array.Empty<string>(), ConversionModes.Encode, duration);
        if (guardResult is not null)
        {
            return guardResult;
        }

        var outputDirectory = Path.GetDirectoryName(outputPath);
        var tempRoot = Path.Combine(
            string.IsNullOrWhiteSpace(outputDirectory) ? Path.GetTempPath() : outputDirectory,
            $"{Path.GetFileNameWithoutExtension(outputPath)}.{Guid.NewGuid():N}.storyboard-combine");
        var technicalDetails = new List<string>
        {
            "Combined storyboard output prototype.",
            "Source-time gaps and overlaps are not preserved; clips are concatenated in StoryboardData order."
        };
        IReadOnlyList<string> finalArguments = Array.Empty<string>();
        ProcessRunResult? finalProcessResult = null;
        var stopwatch = Stopwatch.StartNew();
        var deleteTempRoot = false;
        var tempPreservationLogged = false;

        void LogTempPreservation()
        {
            if (tempPreservationLogged)
            {
                return;
            }

            technicalDetails.Add($"Storyboard temp files preserved for troubleshooting: {tempRoot}");
            tempPreservationLogged = true;
        }

        ConversionResult BuildFailure(string userMessage)
        {
            LogTempPreservation();
            return BuildStoryboardFailureResult(inputPath, outputPath, outputFormat, fps, duration, userMessage, technicalDetails, stopwatch.Elapsed);
        }

        try
        {
            Directory.CreateDirectory(tempRoot);
            progress?.Report(CreateStoryboardPhaseProgress("Preparing storyboard clips...", 0, 1));

            var planningStopwatch = Stopwatch.StartNew();
            var segmentPlan = StoryboardTrimSegmentPlanner.Build(plan, trimRange);
            planningStopwatch.Stop();
            if (!StoryboardTimelineDiagnosticBuilder.PlannerUsesTrimPreviewTimelineBasis(segmentPlan))
            {
                technicalDetails.Add("Timeline mismatch suspected: trim preview and merge planner do not agree.");
                technicalDetails.Add($"Trim preview timeline basis: {StoryboardTrimSegmentPlanner.TimelineMode}");
                technicalDetails.Add($"Merge planner timeline basis: {segmentPlan.TimelineMode}");
                return BuildFailure("Storyboard merge was blocked because trim timeline mapping could not be verified.");
            }

            technicalDetails.Add($"Storyboard trim planning elapsed: {planningStopwatch.Elapsed}.");
            technicalDetails.Add("Storyboard ranged extraction note: DAT byte/frame range extraction is not keyframe-safe yet, so only intersecting source DAT files are full-extracted and then trimmed during normalization.");
            if (segmentPlan.Segments.Count == 0)
            {
                return BuildFailure("Storyboard trim did not intersect any clips.");
            }

            var totalStoryboardPhases = Math.Max(1, (segmentPlan.Segments.Count * 2) + 2);
            var completedStoryboardPhases = 0;
            var h264Paths = new List<string>();
            var intermediatePaths = segmentPlan.Segments
                .Select(segment => Path.Combine(tempRoot, $"segment-{segment.OutputSegmentNumber:000}-clip-{segment.ClipNumber:000}.mp4"))
                .ToList();
            technicalDetails.Add(StoryboardTimelineDiagnosticBuilder.Build(plan, trimRange, segmentPlan, intermediatePaths).Trim());
            for (var index = 0; index < segmentPlan.Segments.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var segment = segmentPlan.Segments[index];
                progress?.Report(CreateStoryboardPhaseProgress($"Extracting storyboard segment {index + 1} of {segmentPlan.Segments.Count}...", completedStoryboardPhases, totalStoryboardPhases));
                var phaseStopwatch = Stopwatch.StartNew();
                var h264Path = Path.Combine(tempRoot, $"segment-{segment.OutputSegmentNumber:000}-clip-{segment.ClipNumber:000}.h264");
                h264Paths.Add(h264Path);
                var extractionResult = await Task.Run(
                    () => extractCleanH264(segment.SourceDatPath, h264Path, cancellationToken),
                    cancellationToken);
                phaseStopwatch.Stop();
                completedStoryboardPhases++;
                technicalDetails.Add($"Segment {segment.OutputSegmentNumber} extraction: clip {segment.ClipNumber}; source={Path.GetFileName(segment.SourceDatPath)}; confident={extractionResult.LookedConfident}; bytes={extractionResult.ExtractedPayloadByteCount}; elapsed={phaseStopwatch.Elapsed}.");
                technicalDetails.Add(extractionResult.BuildTechnicalReport().Trim());
                if (!extractionResult.Succeeded || !extractionResult.LookedConfident || string.IsNullOrWhiteSpace(extractionResult.OutputPath))
                {
                    return BuildFailure($"Storyboard segment {segment.OutputSegmentNumber} could not be extracted.");
                }
            }

            var sourceInputFpsOptions = segmentPlan.Segments
                .Select(segment => ResolveStoryboardSegmentSourceFps(segment, fps, technicalDetails))
                .ToList();
            var canvas = await ProbeH264DimensionsAsync(h264Paths[0], sourceInputFpsOptions[0], cancellationToken);
            technicalDetails.Add($"Storyboard normalization canvas: {canvas.Width}x{canvas.Height}. Resolution policy: scale/pad every clip to the first detected clip resolution.");

            for (var index = 0; index < segmentPlan.Segments.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var segment = segmentPlan.Segments[index];
                var sourceInputFps = sourceInputFpsOptions[index];
                progress?.Report(CreateStoryboardPhaseProgress($"Normalizing storyboard segment {index + 1} of {segmentPlan.Segments.Count}...", completedStoryboardPhases, totalStoryboardPhases));
                var phaseStopwatch = Stopwatch.StartNew();
                var intermediatePath = intermediatePaths[index];
                var intermediateArguments = FfmpegCommandBuilder.BuildStoryboardIntermediateEncodeArguments(
                    h264Paths[index],
                    intermediatePath,
                    sourceInputFps,
                    fps,
                    canvas.Width,
                    canvas.Height,
                    segment.LocalStartOffset,
                    segment.LocalDuration);
                var intermediateResult = await runProcessAsync(
                    ffmpegTools.FfmpegPath,
                    intermediateArguments,
                    ConversionTimeout,
                    cancellationToken,
                    null,
                    null);
                phaseStopwatch.Stop();
                completedStoryboardPhases++;
                var normalizedBytes = TryGetFileLength(intermediatePath);
                technicalDetails.Add($"Segment {segment.OutputSegmentNumber} normalize: clip {segment.ClipNumber}; source_input_fps={sourceInputFps.Label} ({sourceInputFps.FfmpegValue}); final_output_fps={fps.Label} ({fps.FfmpegValue}); local_start={FormatDurationForLog(segment.LocalStartOffset)}; duration={FormatDurationForLog(segment.LocalDuration)}; output_bytes={normalizedBytes}; exit code={FormatExitCode(intermediateResult.ExitCode)}; canceled={intermediateResult.WasCanceled}; timed_out={intermediateResult.TimedOut}; elapsed={phaseStopwatch.Elapsed}.");
                technicalDetails.Add($"Segment {segment.OutputSegmentNumber} normalize command: {ffmpegTools.FfmpegPath} {string.Join(" ", intermediateArguments.Select(QuoteArgumentForLog))}");
                technicalDetails.Add($"Segment {segment.OutputSegmentNumber} normalize stdout: {FormatProcessTextForLog(intermediateResult.StandardOutput)}");
                technicalDetails.Add($"Segment {segment.OutputSegmentNumber} normalize stderr: {FormatProcessTextForLog(intermediateResult.StandardError)}");
                if (intermediateResult.ExitCode != 0 || TryGetFileLength(intermediatePath) <= 0)
                {
                    if (intermediateResult.WasCanceled)
                    {
                        throw new OperationCanceledException(cancellationToken);
                    }

                    technicalDetails.Add(intermediateResult.StandardError.Trim());
                    return BuildFailure($"Storyboard segment {segment.OutputSegmentNumber} could not be normalized.");
                }
            }

            var concatListPath = Path.Combine(tempRoot, "concat.txt");
            progress?.Report(CreateStoryboardPhaseProgress("Preparing storyboard merge...", completedStoryboardPhases, totalStoryboardPhases));
            var concatStopwatch = Stopwatch.StartNew();
            WriteStoryboardConcatList(concatListPath, intermediatePaths);
            var preflight = ValidateStoryboardConcatList(concatListPath, intermediatePaths);
            concatStopwatch.Stop();
            completedStoryboardPhases++;
            technicalDetails.Add($"Storyboard concat list preflight: valid={preflight.IsValid}; elapsed={concatStopwatch.Elapsed}; path={concatListPath}; message={preflight.Message}");
            if (!preflight.IsValid)
            {
                return BuildFailure($"Storyboard merge preflight failed. {preflight.Message}");
            }

            var segmentValidationStopwatch = Stopwatch.StartNew();
            var segmentValidation = await ValidateNormalizedStoryboardSegmentsAsync(intermediatePaths, segmentPlan.Segments, cancellationToken);
            segmentValidationStopwatch.Stop();
            technicalDetails.Add(BuildNormalizedStoryboardSegmentValidationLog(segmentValidation, segmentValidationStopwatch.Elapsed));
            if (!segmentValidation.IsValid)
            {
                technicalDetails.Add($"Normalized storyboard segment validation failed. {segmentValidation.Message}");
                return BuildFailure("Storyboard merge failed while preparing video segments.");
            }

            finalArguments = FfmpegCommandBuilder.BuildStoryboardConcatEncodeArguments(intermediatePaths, outputPath, outputFormat, fps, metadata);
            progress?.Report(CreateStoryboardPhaseProgress("Finalizing storyboard video...", completedStoryboardPhases, totalStoryboardPhases));
            var finalStopwatch = Stopwatch.StartNew();
            var progressParser = new ConversionProgressParser(duration);
            finalProcessResult = await runProcessAsync(
                ffmpegTools.FfmpegPath,
                finalArguments,
                ConversionTimeout,
                cancellationToken,
                line =>
                {
                    var progressUpdate = progressParser.ParseLine(line);
                    if (progressUpdate is not null)
                    {
                        progress?.Report(progressUpdate);
                    }
                },
                null);
            finalStopwatch.Stop();
            technicalDetails.Add($"Storyboard final concat elapsed: {finalStopwatch.Elapsed}.");

            stopwatch.Stop();
            var succeeded = finalProcessResult.ExitCode == 0 && TryGetFileLength(outputPath) > 0;
            if (succeeded)
            {
                deleteTempRoot = true;
                return new ConversionResult(
                    true,
                    "Full storyboard conversion completed.",
                    ffmpegTools.FfmpegPath,
                    finalArguments,
                    inputPath,
                    outputPath,
                    fps,
                    finalProcessResult.ExitCode,
                    finalProcessResult.StandardOutput,
                    PrependTechnicalNote(finalProcessResult.StandardError, string.Join(Environment.NewLine, technicalDetails)),
                    ConversionMode: ConversionModes.Encode,
                    OutputFormat: outputFormat.DisplayName(),
                    TimedOut: finalProcessResult.TimedOut,
                    Duration: duration,
                    UsedDeterminateProgress: duration.HasValue,
                    ProcessingTime: stopwatch.Elapsed,
                    InputPathMode: ConversionInputPathMode.StoryboardCombinedCleanH264);
            }

            var partialOutputMessage = finalProcessResult.WasCanceled
                ? PartialOutputService.TryDeleteCanceledOutput(outputPath, inputPath)
                : PartialOutputService.TryMovePartialOutput(outputPath, inputPath);
            LogTempPreservation();
            return new ConversionResult(
                false,
                finalProcessResult.WasCanceled ? ConversionResult.CanceledMessage : "Full storyboard conversion failed.",
                ffmpegTools.FfmpegPath,
                finalArguments,
                inputPath,
                outputPath,
                fps,
                finalProcessResult.ExitCode,
                finalProcessResult.StandardOutput,
                PrependTechnicalNote(finalProcessResult.StandardError, string.Join(Environment.NewLine, technicalDetails)),
                partialOutputMessage,
                ConversionModes.Encode,
                outputFormat.DisplayName(),
                finalProcessResult.WasCanceled,
                finalProcessResult.TimedOut,
                duration,
                duration.HasValue,
                stopwatch.Elapsed,
                ConversionInputPathMode.StoryboardCombinedCleanH264);
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            var cleanupMessage = PartialOutputService.TryDeleteCanceledOutput(outputPath, inputPath);
            LogTempPreservation();
            return new ConversionResult(
                false,
                ConversionResult.CanceledMessage,
                ffmpegTools.FfmpegPath,
                finalArguments,
                inputPath,
                outputPath,
                fps,
                finalProcessResult?.ExitCode,
                finalProcessResult?.StandardOutput ?? "",
                string.Join(Environment.NewLine, technicalDetails.Concat(new[] { "Combined storyboard conversion was canceled.", cleanupMessage })),
                cleanupMessage,
                ConversionModes.Encode,
                outputFormat.DisplayName(),
                WasCanceled: true,
                Duration: duration,
                UsedDeterminateProgress: duration.HasValue,
                ProcessingTime: stopwatch.Elapsed,
                InputPathMode: ConversionInputPathMode.StoryboardCombinedCleanH264);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            stopwatch.Stop();
            return BuildFailure($"Full storyboard conversion failed. {ex.Message}");
        }
        finally
        {
            if (deleteTempRoot)
            {
                TryDeleteDirectory(tempRoot);
            }
        }
    }

    public async Task<ConversionResult> RemuxTrimmedAsync(
        string inputPath,
        string outputPath,
        OutputFormat outputFormat,
        FpsOption fps,
        TimeSpan? sourceDuration,
        TrimRange trimRange,
        IProgress<ConversionProgress>? progress,
        CancellationToken cancellationToken,
        ContainerMetadata? metadata = null,
        BurnTimestampOptions? burnTimestamp = null)
    {
        if (!HasResolvedFps(fps))
        {
            return BuildUnresolvedFpsResult(inputPath, outputPath, outputFormat, fps, "Fast", trimRange.End - trimRange.Start);
        }

        if (!trimRange.TryValidate(sourceDuration, out var validationMessage))
        {
            return BuildTrimFailureResult(inputPath, outputPath, outputFormat, fps, trimRange, validationMessage ?? "Trim range is invalid.");
        }

        var guardResult = BuildOutputGuardResult(inputPath, outputPath, outputFormat, fps, Array.Empty<string>(), "Fast", trimRange.End - trimRange.Start);
        if (guardResult is not null)
        {
            return guardResult;
        }

        var tempH264Path = BuildTempH264Path(outputPath, "trim-remux");
        try
        {
            var extractionResult = await Task.Run(
                () => extractTrimmedH264(inputPath, trimRange.Start, trimRange.End, sourceDuration, tempH264Path, cancellationToken),
                cancellationToken);
            if (!extractionResult.Succeeded || string.IsNullOrWhiteSpace(extractionResult.OutputPath))
            {
                TryDeleteFile(outputPath);
                return BuildTrimFailureResult(inputPath, outputPath, outputFormat, fps, trimRange, $"Trim Video could not be applied. {extractionResult.FailureReason ?? "No trimmed H.264 data was produced."}", extractionResult.TechnicalDetails);
            }

            var arguments = SpotterCleanRemuxPrototype.BuildCleanRemuxArguments(tempH264Path, outputPath, outputFormat, fps, metadata);
            var result = await RunConversionAsync(
                inputPath,
                outputPath,
                outputFormat,
                fps,
                arguments,
                "Fast",
                "Fast trimmed conversion completed.",
                "Trim Video Fast mode failed.",
                trimRange.End - trimRange.Start,
                progress,
                cancellationToken,
                ConversionInputPathMode.TrimmedCleanH264);

            return result with
            {
                StandardError = PrependTechnicalNote(
                    result.StandardError,
                    BuildTrimTechnicalNote(trimRange, extractionResult))
            };
        }
        catch (OperationCanceledException)
        {
            TryDeleteFile(outputPath);
            return new ConversionResult(
                false,
                ConversionResult.CanceledMessage,
                ffmpegTools.FfmpegPath,
                Array.Empty<string>(),
                inputPath,
                outputPath,
                fps,
                null,
                "",
                "Trimmed Fast conversion canceled during DAT frame extraction.",
                ConversionMode: "Fast",
                OutputFormat: outputFormat.DisplayName(),
                WasCanceled: true,
                Duration: trimRange.End - trimRange.Start,
                UsedDeterminateProgress: true,
                InputPathMode: ConversionInputPathMode.TrimmedCleanH264);
        }
        finally
        {
            TryDeleteFile(tempH264Path);
        }
    }

    public async Task<ConversionResult> RemuxTrimmedSplitAsync(
        string inputPath,
        string outputPath,
        OutputFormat outputFormat,
        FpsOption fps,
        SpotterSplitExportPlan plan,
        RecordingTimeline timeline,
        TrimRange trimRange,
        CancellationToken cancellationToken,
        ContainerMetadata? metadata = null)
    {
        if (!HasResolvedFps(fps))
        {
            return BuildUnresolvedFpsResult(inputPath, outputPath, outputFormat, fps, "Fast", trimRange.End - trimRange.Start);
        }

        if (!plan.IsStrongConfidence)
        {
            return BuildTrimFailureResult(inputPath, outputPath, outputFormat, fps, trimRange, "Split recording could not be verified.");
        }

        if (!trimRange.TryValidate(timeline.TotalDuration, out var validationMessage))
        {
            return BuildTrimFailureResult(inputPath, outputPath, outputFormat, fps, trimRange, validationMessage ?? "Trim range is invalid.");
        }

        var guardResult = BuildOutputGuardResult(inputPath, outputPath, outputFormat, fps, Array.Empty<string>(), "Fast", trimRange.End - trimRange.Start);
        if (guardResult is not null)
        {
            return guardResult;
        }

        var combinedTempPath = BuildTempH264Path(outputPath, "trimmed-split-remux");
        var segmentTempPaths = new List<string>();
        var technicalDetails = new List<string>();
        try
        {
            using (var combinedStream = new FileStream(combinedTempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                foreach (var segment in GetTrimmedSegments(timeline, trimRange))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var segmentTempPath = BuildTempH264Path(outputPath, $"trimmed-segment-{segment.SegmentIndex:000}");
                    segmentTempPaths.Add(segmentTempPath);
                    var extractionResult = await Task.Run(
                        () => extractTrimmedH264(segment.SourcePath, segment.LocalStart, segment.LocalEnd, segment.Duration, segmentTempPath, cancellationToken),
                        cancellationToken);
                    technicalDetails.Add(extractionResult.TechnicalDetails);
                    if (!extractionResult.Succeeded || string.IsNullOrWhiteSpace(extractionResult.OutputPath))
                    {
                        TryDeleteFile(outputPath);
                        return BuildTrimFailureResult(inputPath, outputPath, outputFormat, fps, trimRange, $"Trim Video could not be applied to split segment: {Path.GetFileName(segment.SourcePath)}. {extractionResult.FailureReason ?? "No trimmed H.264 data was produced."}", string.Join(Environment.NewLine, technicalDetails));
                    }

                    using var segmentStream = new FileStream(extractionResult.OutputPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    segmentStream.CopyTo(combinedStream);
                }
            }

            if (TryGetFileLength(combinedTempPath) <= 0)
            {
                return BuildTrimFailureResult(inputPath, outputPath, outputFormat, fps, trimRange, "Trim Video could not be applied. No split segment payloads were selected.", string.Join(Environment.NewLine, technicalDetails));
            }

            var arguments = SpotterCleanRemuxPrototype.BuildCleanRemuxArguments(combinedTempPath, outputPath, outputFormat, fps, metadata);
            var result = await RunConversionAsync(
                inputPath,
                outputPath,
                outputFormat,
                fps,
                arguments,
                "Fast",
                "Fast trimmed conversion completed.",
                "Trim Video Fast mode failed.",
                trimRange.End - trimRange.Start,
                progress: null,
                cancellationToken,
                ConversionInputPathMode.TrimmedCleanH264);

            return result with
            {
                StandardError = PrependTechnicalNote(
                    result.StandardError,
                    BuildSplitTrimTechnicalNote(trimRange, technicalDetails))
            };
        }
        catch (OperationCanceledException)
        {
            TryDeleteFile(outputPath);
            return new ConversionResult(
                false,
                ConversionResult.CanceledMessage,
                ffmpegTools.FfmpegPath,
                Array.Empty<string>(),
                inputPath,
                outputPath,
                fps,
                null,
                "",
                "Trimmed split Fast conversion canceled during DAT frame extraction.",
                ConversionMode: "Fast",
                OutputFormat: outputFormat.DisplayName(),
                WasCanceled: true,
                Duration: trimRange.End - trimRange.Start,
                UsedDeterminateProgress: true,
                InputPathMode: ConversionInputPathMode.TrimmedCleanH264);
        }
        finally
        {
            foreach (var path in segmentTempPaths)
            {
                TryDeleteFile(path);
            }

            TryDeleteFile(combinedTempPath);
        }
    }

    public async Task<ConversionResult> EncodeTrimmedAsync(
        string inputPath,
        string outputPath,
        OutputFormat outputFormat,
        FpsOption fps,
        TimeSpan? sourceDuration,
        TrimRange trimRange,
        IProgress<ConversionProgress>? progress,
        CancellationToken cancellationToken,
        ContainerMetadata? metadata = null,
        BurnTimestampOptions? burnTimestamp = null)
    {
        if (!HasResolvedFps(fps))
        {
            return BuildUnresolvedFpsResult(inputPath, outputPath, outputFormat, fps, "Encode", trimRange.End - trimRange.Start);
        }

        if (!trimRange.TryValidate(sourceDuration, out var validationMessage))
        {
            return BuildTrimFailureResult(inputPath, outputPath, outputFormat, fps, trimRange, validationMessage ?? "Trim range is invalid.", conversionMode: "Encode");
        }

        var guardResult = BuildOutputGuardResult(inputPath, outputPath, outputFormat, fps, Array.Empty<string>(), "Encode", trimRange.End - trimRange.Start);
        if (guardResult is not null)
        {
            return guardResult;
        }

        var tempH264Path = BuildTempH264Path(outputPath, "trim-encode");
        try
        {
            ReportPreparationProgress(progress);
            var extractionResult = await Task.Run(
                () => extractTrimmedH264(inputPath, trimRange.Start, trimRange.End, sourceDuration, tempH264Path, cancellationToken),
                cancellationToken);
            if (!extractionResult.Succeeded || string.IsNullOrWhiteSpace(extractionResult.OutputPath))
            {
                TryDeleteFile(outputPath);
                return BuildTrimFailureResult(inputPath, outputPath, outputFormat, fps, trimRange, $"Trim Video could not be applied. {extractionResult.FailureReason ?? "No trimmed H.264 data was produced."}", extractionResult.TechnicalDetails, "Encode");
            }

            var preRoll = CalculatePreRoll(trimRange.Start, extractionResult.SelectedKeyframeLocalOffset);
            var arguments = FfmpegCommandBuilder.BuildTrimEncodeArguments(tempH264Path, outputPath, outputFormat, fps, preRoll, trimRange.End - trimRange.Start, metadata, burnTimestamp);
            ReportEncodeStartProgress(progress);
            var result = await RunConversionAsync(
                inputPath,
                outputPath,
                outputFormat,
                fps,
                arguments,
                "Encode",
                "Full trimmed conversion completed.",
                "Trim Video Full mode failed.",
                trimRange.End - trimRange.Start,
                progress,
                cancellationToken,
                ConversionInputPathMode.TrimmedCleanH264);

            var resultWithFontWarning = AppendBurnTimestampFontWarning(result, burnTimestamp);
            return resultWithFontWarning with
            {
                StandardError = PrependTechnicalNote(
                    resultWithFontWarning.StandardError,
                    BuildTrimEncodeTechnicalNote(trimRange, preRoll, extractionResult))
            };
        }
        catch (OperationCanceledException)
        {
            TryDeleteFile(outputPath);
            return new ConversionResult(
                false,
                ConversionResult.CanceledMessage,
                ffmpegTools.FfmpegPath,
                Array.Empty<string>(),
                inputPath,
                outputPath,
                fps,
                null,
                "",
                "Trimmed Full conversion canceled during DAT frame extraction.",
                ConversionMode: "Encode",
                OutputFormat: outputFormat.DisplayName(),
                WasCanceled: true,
                Duration: trimRange.End - trimRange.Start,
                UsedDeterminateProgress: true,
                InputPathMode: ConversionInputPathMode.TrimmedCleanH264);
        }
        finally
        {
            TryDeleteFile(tempH264Path);
        }
    }

    public async Task<ConversionResult> EncodeTrimmedNvencAsync(
        string inputPath,
        string outputPath,
        OutputFormat outputFormat,
        FpsOption fps,
        TimeSpan? sourceDuration,
        TrimRange trimRange,
        IProgress<ConversionProgress>? progress,
        CancellationToken cancellationToken,
        ContainerMetadata? metadata = null,
        BurnTimestampOptions? burnTimestamp = null)
    {
        if (!HasResolvedFps(fps))
        {
            return BuildUnresolvedFpsResult(inputPath, outputPath, outputFormat, fps, ConversionModes.EncodeNvenc, trimRange.End - trimRange.Start);
        }

        if (!trimRange.TryValidate(sourceDuration, out var validationMessage))
        {
            return BuildTrimFailureResult(inputPath, outputPath, outputFormat, fps, trimRange, validationMessage ?? "Trim range is invalid.", conversionMode: ConversionModes.EncodeNvenc);
        }

        var guardResult = BuildOutputGuardResult(inputPath, outputPath, outputFormat, fps, Array.Empty<string>(), ConversionModes.EncodeNvenc, trimRange.End - trimRange.Start);
        if (guardResult is not null)
        {
            return guardResult;
        }

        var tempH264Path = BuildTempH264Path(outputPath, "trim-nvenc-encode");
        try
        {
            ReportPreparationProgress(progress);
            var extractionResult = await Task.Run(
                () => extractTrimmedH264(inputPath, trimRange.Start, trimRange.End, sourceDuration, tempH264Path, cancellationToken),
                cancellationToken);
            if (!extractionResult.Succeeded || string.IsNullOrWhiteSpace(extractionResult.OutputPath))
            {
                TryDeleteFile(outputPath);
                return BuildTrimFailureResult(inputPath, outputPath, outputFormat, fps, trimRange, $"Trim Video could not be applied. {extractionResult.FailureReason ?? "No trimmed H.264 data was produced."}", extractionResult.TechnicalDetails, ConversionModes.EncodeNvenc);
            }

            var preRoll = CalculatePreRoll(trimRange.Start, extractionResult.SelectedKeyframeLocalOffset);
            var arguments = FfmpegCommandBuilder.BuildTrimNvencEncodeArguments(tempH264Path, outputPath, outputFormat, fps, preRoll, trimRange.End - trimRange.Start, metadata, burnTimestamp);
            ReportEncodeStartProgress(progress);
            var result = await RunConversionAsync(
                inputPath,
                outputPath,
                outputFormat,
                fps,
                arguments,
                ConversionModes.EncodeNvenc,
                "Full NVENC trimmed conversion completed.",
                NvencUnavailableMessage,
                trimRange.End - trimRange.Start,
                progress,
                cancellationToken,
                ConversionInputPathMode.TrimmedCleanH264);

            var resultWithFontWarning = AppendBurnTimestampFontWarning(result, burnTimestamp);
            return resultWithFontWarning with
            {
                StandardError = PrependTechnicalNote(
                    resultWithFontWarning.StandardError,
                    BuildTrimEncodeTechnicalNote(trimRange, preRoll, extractionResult))
            };
        }
        catch (OperationCanceledException)
        {
            TryDeleteFile(outputPath);
            return new ConversionResult(
                false,
                ConversionResult.CanceledMessage,
                ffmpegTools.FfmpegPath,
                Array.Empty<string>(),
                inputPath,
                outputPath,
                fps,
                null,
                "",
                "Trimmed Full NVENC conversion canceled during DAT frame extraction.",
                ConversionMode: ConversionModes.EncodeNvenc,
                OutputFormat: outputFormat.DisplayName(),
                WasCanceled: true,
                Duration: trimRange.End - trimRange.Start,
                UsedDeterminateProgress: true,
                InputPathMode: ConversionInputPathMode.TrimmedCleanH264);
        }
        finally
        {
            TryDeleteFile(tempH264Path);
        }
    }

    public async Task<ConversionResult> EncodeTrimmedSplitAsync(
        string inputPath,
        string outputPath,
        OutputFormat outputFormat,
        FpsOption fps,
        SpotterSplitExportPlan plan,
        RecordingTimeline timeline,
        TrimRange trimRange,
        CancellationToken cancellationToken,
        ContainerMetadata? metadata = null,
        BurnTimestampOptions? burnTimestamp = null,
        IProgress<ConversionProgress>? progress = null)
    {
        if (!HasResolvedFps(fps))
        {
            return BuildUnresolvedFpsResult(inputPath, outputPath, outputFormat, fps, "Encode", trimRange.End - trimRange.Start);
        }

        if (!plan.IsStrongConfidence)
        {
            return BuildTrimFailureResult(inputPath, outputPath, outputFormat, fps, trimRange, "Split recording could not be verified.", conversionMode: "Encode");
        }

        if (!trimRange.TryValidate(timeline.TotalDuration, out var validationMessage))
        {
            return BuildTrimFailureResult(inputPath, outputPath, outputFormat, fps, trimRange, validationMessage ?? "Trim range is invalid.", conversionMode: "Encode");
        }

        var guardResult = BuildOutputGuardResult(inputPath, outputPath, outputFormat, fps, Array.Empty<string>(), "Encode", trimRange.End - trimRange.Start);
        if (guardResult is not null)
        {
            return guardResult;
        }

        var combinedTempPath = BuildTempH264Path(outputPath, "trimmed-split-encode");
        var segmentTempPaths = new List<string>();
        var technicalDetails = new List<string>();
        var preRoll = TimeSpan.Zero;
        var capturedFirstPreRoll = false;
        try
        {
            ReportPreparationProgress(progress);
            using (var combinedStream = new FileStream(combinedTempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                foreach (var segment in GetTrimmedSegments(timeline, trimRange))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var segmentTempPath = BuildTempH264Path(outputPath, $"trimmed-encode-segment-{segment.SegmentIndex:000}");
                    segmentTempPaths.Add(segmentTempPath);
                    var extractionResult = await Task.Run(
                        () => extractTrimmedH264(segment.SourcePath, segment.LocalStart, segment.LocalEnd, segment.Duration, segmentTempPath, cancellationToken),
                        cancellationToken);
                    technicalDetails.Add(extractionResult.TechnicalDetails);
                    if (!extractionResult.Succeeded || string.IsNullOrWhiteSpace(extractionResult.OutputPath))
                    {
                        TryDeleteFile(outputPath);
                        return BuildTrimFailureResult(inputPath, outputPath, outputFormat, fps, trimRange, $"Trim Video could not be applied to split segment: {Path.GetFileName(segment.SourcePath)}. {extractionResult.FailureReason ?? "No trimmed H.264 data was produced."}", string.Join(Environment.NewLine, technicalDetails), "Encode");
                    }

                    if (!capturedFirstPreRoll)
                    {
                        var selectedKeyframe = extractionResult.SelectedKeyframeLocalOffset ?? segment.LocalStart;
                        preRoll = CalculatePreRoll(trimRange.Start, segment.GlobalStart + selectedKeyframe);
                        capturedFirstPreRoll = true;
                    }

                    using var segmentStream = new FileStream(extractionResult.OutputPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    segmentStream.CopyTo(combinedStream);
                }
            }

            if (TryGetFileLength(combinedTempPath) <= 0)
            {
                return BuildTrimFailureResult(inputPath, outputPath, outputFormat, fps, trimRange, "Trim Video could not be applied. No split segment payloads were selected.", string.Join(Environment.NewLine, technicalDetails), "Encode");
            }

            var arguments = FfmpegCommandBuilder.BuildTrimEncodeArguments(combinedTempPath, outputPath, outputFormat, fps, preRoll, trimRange.End - trimRange.Start, metadata, burnTimestamp);
            ReportEncodeStartProgress(progress);
            var result = await RunConversionAsync(
                inputPath,
                outputPath,
                outputFormat,
                fps,
                arguments,
                "Encode",
                "Full trimmed conversion completed.",
                "Trim Video Full mode failed.",
                trimRange.End - trimRange.Start,
                progress,
                cancellationToken,
                ConversionInputPathMode.TrimmedCleanH264);

            var resultWithFontWarning = AppendBurnTimestampFontWarning(result, burnTimestamp);
            return resultWithFontWarning with
            {
                StandardError = PrependTechnicalNote(
                    resultWithFontWarning.StandardError,
                    BuildSplitTrimEncodeTechnicalNote(trimRange, preRoll, technicalDetails))
            };
        }
        catch (OperationCanceledException)
        {
            TryDeleteFile(outputPath);
            return new ConversionResult(
                false,
                ConversionResult.CanceledMessage,
                ffmpegTools.FfmpegPath,
                Array.Empty<string>(),
                inputPath,
                outputPath,
                fps,
                null,
                "",
                "Trimmed split Full conversion canceled during DAT frame extraction.",
                ConversionMode: "Encode",
                OutputFormat: outputFormat.DisplayName(),
                WasCanceled: true,
                Duration: trimRange.End - trimRange.Start,
                UsedDeterminateProgress: true,
                InputPathMode: ConversionInputPathMode.TrimmedCleanH264);
        }
        finally
        {
            foreach (var path in segmentTempPaths)
            {
                TryDeleteFile(path);
            }

            TryDeleteFile(combinedTempPath);
        }
    }

    public async Task<ConversionResult> EncodeTrimmedSplitNvencAsync(
        string inputPath,
        string outputPath,
        OutputFormat outputFormat,
        FpsOption fps,
        SpotterSplitExportPlan plan,
        RecordingTimeline timeline,
        TrimRange trimRange,
        CancellationToken cancellationToken,
        ContainerMetadata? metadata = null,
        BurnTimestampOptions? burnTimestamp = null,
        IProgress<ConversionProgress>? progress = null)
    {
        if (!HasResolvedFps(fps))
        {
            return BuildUnresolvedFpsResult(inputPath, outputPath, outputFormat, fps, ConversionModes.EncodeNvenc, trimRange.End - trimRange.Start);
        }

        if (!plan.IsStrongConfidence)
        {
            return BuildTrimFailureResult(inputPath, outputPath, outputFormat, fps, trimRange, "Split recording could not be verified.", conversionMode: ConversionModes.EncodeNvenc);
        }

        if (!trimRange.TryValidate(timeline.TotalDuration, out var validationMessage))
        {
            return BuildTrimFailureResult(inputPath, outputPath, outputFormat, fps, trimRange, validationMessage ?? "Trim range is invalid.", conversionMode: ConversionModes.EncodeNvenc);
        }

        var guardResult = BuildOutputGuardResult(inputPath, outputPath, outputFormat, fps, Array.Empty<string>(), ConversionModes.EncodeNvenc, trimRange.End - trimRange.Start);
        if (guardResult is not null)
        {
            return guardResult;
        }

        var combinedTempPath = BuildTempH264Path(outputPath, "trimmed-split-nvenc-encode");
        var segmentTempPaths = new List<string>();
        var technicalDetails = new List<string>();
        var preRoll = TimeSpan.Zero;
        var capturedFirstPreRoll = false;
        try
        {
            ReportPreparationProgress(progress);
            using (var combinedStream = new FileStream(combinedTempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                foreach (var segment in GetTrimmedSegments(timeline, trimRange))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var segmentTempPath = BuildTempH264Path(outputPath, $"trimmed-nvenc-encode-segment-{segment.SegmentIndex:000}");
                    segmentTempPaths.Add(segmentTempPath);
                    var extractionResult = await Task.Run(
                        () => extractTrimmedH264(segment.SourcePath, segment.LocalStart, segment.LocalEnd, segment.Duration, segmentTempPath, cancellationToken),
                        cancellationToken);
                    technicalDetails.Add(extractionResult.TechnicalDetails);
                    if (!extractionResult.Succeeded || string.IsNullOrWhiteSpace(extractionResult.OutputPath))
                    {
                        TryDeleteFile(outputPath);
                        return BuildTrimFailureResult(inputPath, outputPath, outputFormat, fps, trimRange, $"Trim Video could not be applied to split segment: {Path.GetFileName(segment.SourcePath)}. {extractionResult.FailureReason ?? "No trimmed H.264 data was produced."}", string.Join(Environment.NewLine, technicalDetails), ConversionModes.EncodeNvenc);
                    }

                    if (!capturedFirstPreRoll)
                    {
                        var selectedKeyframe = extractionResult.SelectedKeyframeLocalOffset ?? segment.LocalStart;
                        preRoll = CalculatePreRoll(trimRange.Start, segment.GlobalStart + selectedKeyframe);
                        capturedFirstPreRoll = true;
                    }

                    using var segmentStream = new FileStream(extractionResult.OutputPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    segmentStream.CopyTo(combinedStream);
                }
            }

            if (TryGetFileLength(combinedTempPath) <= 0)
            {
                return BuildTrimFailureResult(inputPath, outputPath, outputFormat, fps, trimRange, "Trim Video could not be applied. No split segment payloads were selected.", string.Join(Environment.NewLine, technicalDetails), ConversionModes.EncodeNvenc);
            }

            var arguments = FfmpegCommandBuilder.BuildTrimNvencEncodeArguments(combinedTempPath, outputPath, outputFormat, fps, preRoll, trimRange.End - trimRange.Start, metadata, burnTimestamp);
            ReportEncodeStartProgress(progress);
            var result = await RunConversionAsync(
                inputPath,
                outputPath,
                outputFormat,
                fps,
                arguments,
                ConversionModes.EncodeNvenc,
                "Full NVENC trimmed conversion completed.",
                NvencUnavailableMessage,
                trimRange.End - trimRange.Start,
                progress,
                cancellationToken,
                ConversionInputPathMode.TrimmedCleanH264);

            var resultWithFontWarning = AppendBurnTimestampFontWarning(result, burnTimestamp);
            return resultWithFontWarning with
            {
                StandardError = PrependTechnicalNote(
                    resultWithFontWarning.StandardError,
                    BuildSplitTrimEncodeTechnicalNote(trimRange, preRoll, technicalDetails))
            };
        }
        catch (OperationCanceledException)
        {
            TryDeleteFile(outputPath);
            return new ConversionResult(
                false,
                ConversionResult.CanceledMessage,
                ffmpegTools.FfmpegPath,
                Array.Empty<string>(),
                inputPath,
                outputPath,
                fps,
                null,
                "",
                "Trimmed split Full NVENC conversion canceled during DAT frame extraction.",
                ConversionMode: ConversionModes.EncodeNvenc,
                OutputFormat: outputFormat.DisplayName(),
                WasCanceled: true,
                Duration: trimRange.End - trimRange.Start,
                UsedDeterminateProgress: true,
                InputPathMode: ConversionInputPathMode.TrimmedCleanH264);
        }
        finally
        {
            foreach (var path in segmentTempPaths)
            {
                TryDeleteFile(path);
            }

            TryDeleteFile(combinedTempPath);
        }
    }

    private async Task<ConversionResult> RemuxWithCleanExtractedH264Async(
        string inputPath,
        string outputPath,
        OutputFormat outputFormat,
        FpsOption fps,
        TimeSpan? duration,
        IProgress<ConversionProgress>? progress,
        CancellationToken cancellationToken,
        ContainerMetadata? metadata)
    {
        var fallbackArguments = FfmpegCommandBuilder.BuildRemuxArguments(inputPath, outputPath, outputFormat, fps, metadata);
        var guardResult = BuildOutputGuardResult(inputPath, outputPath, outputFormat, fps, fallbackArguments, "Fast", duration);
        if (guardResult is not null)
        {
            return guardResult;
        }

        var tempH264Path = BuildTempH264Path(outputPath);
        try
        {
            var extractionResult = await Task.Run(
                () => extractCleanH264(inputPath, tempH264Path, cancellationToken),
                cancellationToken);
            if (!extractionResult.Succeeded || !extractionResult.LookedConfident)
            {
                return await RunStandardRemuxFallbackAsync(
                    inputPath,
                    outputPath,
                    outputFormat,
                    fps,
                    duration,
                    progress,
                    cancellationToken,
                    metadata,
                    "Internal clean remux fallback: clean H.264 extraction was not confident enough.");
            }

            var arguments = FfmpegCommandBuilder.BuildRemuxArguments(tempH264Path, outputPath, outputFormat, fps, metadata);
            var result = await RunConversionAsync(
                inputPath,
                outputPath,
                outputFormat,
                fps,
                arguments,
                "Fast",
                "Fast conversion completed.",
                ConversionResult.FastFailedMessage,
                duration,
                progress,
                cancellationToken,
                ConversionInputPathMode.CleanExtractedH264);

            return result with
            {
                StandardError = PrependTechnicalNote(
                    result.StandardError,
                    $"Internal clean remux path used. Temporary H.264 input was extracted before FFmpeg. Temporary disk space may approach the extracted video size ({extractionResult.ExtractedPayloadByteCount} bytes).")
            };
        }
        catch (OperationCanceledException)
        {
            return new ConversionResult(
                false,
                ConversionResult.CanceledMessage,
                ffmpegTools.FfmpegPath,
                Array.Empty<string>(),
                inputPath,
                outputPath,
                fps,
                null,
                "",
                "Internal clean remux canceled during H.264 extraction before FFmpeg started.",
                ConversionMode: "Fast",
                OutputFormat: outputFormat.DisplayName(),
                WasCanceled: true,
                Duration: duration,
                UsedDeterminateProgress: duration.HasValue,
                InputPathMode: ConversionInputPathMode.CleanExtractedH264);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return await RunStandardRemuxFallbackAsync(
                inputPath,
                outputPath,
                outputFormat,
                fps,
                duration,
                progress,
                cancellationToken,
                metadata,
                $"Internal clean remux fallback: clean H.264 extraction failed before FFmpeg. {ex.Message}");
        }
        finally
        {
            TryDeleteFile(tempH264Path);
        }
    }

    private async Task<ConversionResult> RunStandardRemuxFallbackAsync(
        string inputPath,
        string outputPath,
        OutputFormat outputFormat,
        FpsOption fps,
        TimeSpan? duration,
        IProgress<ConversionProgress>? progress,
        CancellationToken cancellationToken,
        ContainerMetadata? metadata,
        string note)
    {
        var arguments = FfmpegCommandBuilder.BuildRemuxArguments(inputPath, outputPath, outputFormat, fps, metadata);
        var result = await RunConversionAsync(
            inputPath,
            outputPath,
            outputFormat,
            fps,
            arguments,
            "Fast",
            "Fast conversion completed.",
            ConversionResult.FastFailedMessage,
            duration,
            progress,
            cancellationToken,
            ConversionInputPathMode.StandardWholeDatRawH264);

        return result with
        {
            StandardError = PrependTechnicalNote(result.StandardError, note)
        };
    }

    private async Task<ConversionResult> RunConversionAsync(
        string inputPath,
        string outputPath,
        OutputFormat outputFormat,
        FpsOption fps,
        IReadOnlyList<string> arguments,
        string conversionMode,
        string successMessage,
        string failureMessage,
        TimeSpan? duration,
        IProgress<ConversionProgress>? progress,
        CancellationToken cancellationToken,
        ConversionInputPathMode inputPathMode)
    {
        var guardResult = BuildOutputGuardResult(inputPath, outputPath, outputFormat, fps, arguments, conversionMode, duration);
        if (guardResult is not null)
        {
            return guardResult;
        }

        var hadSidecarPartialBeforeConversion = File.Exists(outputPath + ".partial");
        var progressParser = new ConversionProgressParser(duration);
        var stopwatch = Stopwatch.StartNew();
        var processResult = await runProcessAsync(
            ffmpegTools.FfmpegPath,
            arguments,
            ConversionTimeout,
            cancellationToken,
            line =>
            {
                var progressUpdate = progressParser.ParseLine(line);
                if (progressUpdate is not null)
                {
                    progress?.Report(progressUpdate);
                }
            },
            null);
        stopwatch.Stop();
        var processingTime = stopwatch.Elapsed;
        var succeeded = processResult.ExitCode == 0 && TryGetFileLength(outputPath) > 0;
        var telemetry = ConversionTelemetry.Build(
            inputPath,
            outputPath,
            outputFormat,
            conversionMode,
            fps,
            duration,
            processingTime,
            processResult.ExitCode,
            succeeded,
            processResult.WasCanceled,
            processResult.StandardError,
            progressParser.LastProgress,
            inputPathMode,
            arguments);

        if (succeeded)
        {
            return new ConversionResult(
                true,
                successMessage,
                ffmpegTools.FfmpegPath,
                arguments,
                inputPath,
                outputPath,
                fps,
                processResult.ExitCode,
                processResult.StandardOutput,
                processResult.StandardError,
                ConversionMode: conversionMode,
                OutputFormat: outputFormat.DisplayName(),
                TimedOut: processResult.TimedOut,
                Duration: duration,
                UsedDeterminateProgress: duration.HasValue,
                ProcessingTime: processingTime,
                InputPathMode: inputPathMode,
                Telemetry: telemetry);
        }

        var partialOutputMessage = processResult.WasCanceled
            ? PartialOutputService.TryDeleteCanceledOutput(outputPath, inputPath, deleteSidecarPartial: !hadSidecarPartialBeforeConversion)
            : PartialOutputService.TryMovePartialOutput(outputPath, inputPath);
        var userMessage = processResult.WasCanceled
            ? ConversionResult.CanceledMessage
            : ResolveConversionFailureMessage(arguments, failureMessage);
        return new ConversionResult(
            false,
            userMessage,
            ffmpegTools.FfmpegPath,
            arguments,
            inputPath,
            outputPath,
            fps,
            processResult.ExitCode,
            processResult.StandardOutput,
            processResult.StandardError,
            partialOutputMessage,
            conversionMode,
            outputFormat.DisplayName(),
            processResult.WasCanceled,
            processResult.TimedOut,
            duration,
            duration.HasValue,
            processingTime,
            inputPathMode,
            telemetry);
    }

    private static string ResolveConversionFailureMessage(IReadOnlyList<string> arguments, string failureMessage)
    {
        if (UsesNvencEncoder(arguments))
        {
            return NvencUnavailableMessage;
        }

        return UsesBurnTimestampFilter(arguments)
            ? BurnTimestampMetadataBuilder.BundledFfmpegUnavailableMessage
            : failureMessage;
    }

    private static bool UsesNvencEncoder(IReadOnlyList<string> arguments)
    {
        return arguments.Any(argument => string.Equals(argument, "h264_nvenc", StringComparison.OrdinalIgnoreCase));
    }

    private static bool UsesBurnTimestampFilter(IReadOnlyList<string> arguments)
    {
        return arguments.Any(argument => argument.Contains("drawtext", StringComparison.OrdinalIgnoreCase));
    }

    private static void ReportPreparationProgress(IProgress<ConversionProgress>? progress)
    {
        progress?.Report(new ConversionProgress(null, null, null, null, false, "Preparing selected trim..."));
    }

    private static void ReportEncodeStartProgress(IProgress<ConversionProgress>? progress)
    {
        progress?.Report(new ConversionProgress(null, null, null, null, false, "Encoding selected trim..."));
    }

    private static ConversionProgress CreateIndeterminateProgress(string summary)
    {
        return new ConversionProgress(null, null, null, null, false, summary, IsIndeterminate: true);
    }

    private static ConversionProgress CreateStoryboardPhaseProgress(string summary, int completedPhases, int totalPhases)
    {
        var percent = totalPhases <= 0
            ? 0
            : (int)Math.Clamp(Math.Round(completedPhases * 100d / totalPhases), 0, 99);
        return new ConversionProgress(percent, null, null, null, false, summary);
    }

    private static ConversionResult AppendBurnTimestampFontWarning(
        ConversionResult result,
        BurnTimestampOptions? burnTimestamp)
    {
        if (string.IsNullOrWhiteSpace(burnTimestamp?.FontWarning))
        {
            return result;
        }

        return result with
        {
            StandardError = PrependTechnicalNote(result.StandardError, burnTimestamp.FontWarning)
        };
    }

    private ConversionResult? BuildOutputGuardResult(
        string inputPath,
        string outputPath,
        OutputFormat outputFormat,
        FpsOption fps,
        IReadOnlyList<string> arguments,
        string conversionMode,
        TimeSpan? duration)
    {
        if (!OutputPathService.IsSafeOutputPath(inputPath, outputPath))
        {
            return new ConversionResult(
                false,
                "Conversion blocked because the output path matches the source .dat file.",
                ffmpegTools.FfmpegPath,
                arguments,
                inputPath,
                outputPath,
                fps,
                null,
                "",
                "Output path safety guard blocked conversion before FFmpeg started.",
                ConversionMode: conversionMode,
                OutputFormat: outputFormat.DisplayName(),
                Duration: duration,
                UsedDeterminateProgress: duration.HasValue);
        }

        if (File.Exists(outputPath))
        {
            return new ConversionResult(
                false,
                "Conversion blocked because the output file already exists.",
                ffmpegTools.FfmpegPath,
                arguments,
                inputPath,
                outputPath,
                fps,
                null,
                "",
                "Output path collision guard blocked conversion before FFmpeg started.",
                ConversionMode: conversionMode,
                OutputFormat: outputFormat.DisplayName(),
                Duration: duration,
                UsedDeterminateProgress: duration.HasValue);
        }

        return null;
    }

    private static long TryGetFileLength(string path)
    {
        try
        {
            return File.Exists(path) ? new FileInfo(path).Length : 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or PathTooLongException or NotSupportedException)
        {
            return 0;
        }
    }

    private static bool HasResolvedFps(FpsOption fps)
    {
        return !string.IsNullOrWhiteSpace(fps.FfmpegValue);
    }

    private bool ShouldUseCleanRemux(string inputPath, OutputFormat outputFormat)
    {
        return !internalOptions.DisableCleanRemux &&
               string.Equals(Path.GetExtension(inputPath), ".dat", StringComparison.OrdinalIgnoreCase) &&
               outputFormat is OutputFormat.Mp4 or OutputFormat.Mkv;
    }

    private static string BuildTempH264Path(string outputPath)
    {
        return BuildTempH264Path(outputPath, "internal-clean-remux");
    }

    private static string BuildTempH264Path(string outputPath, string purpose)
    {
        var outputDirectory = Path.GetDirectoryName(outputPath);
        var tempDirectory = string.IsNullOrWhiteSpace(outputDirectory) ? Path.GetTempPath() : outputDirectory;
        return Path.Combine(tempDirectory, $"{Path.GetFileNameWithoutExtension(outputPath)}.{Guid.NewGuid():N}.{purpose}.h264");
    }

    private static IReadOnlyList<TrimmedSegmentRange> GetTrimmedSegments(RecordingTimeline timeline, TrimRange trimRange)
    {
        var result = new List<TrimmedSegmentRange>();
        for (var index = 0; index < timeline.Segments.Count; index++)
        {
            var segment = timeline.Segments[index];
            if (!segment.Duration.HasValue)
            {
                continue;
            }

            var segmentStart = segment.ElapsedOffset;
            var segmentEnd = segmentStart + segment.Duration.Value;
            var overlapStart = Max(trimRange.Start, segmentStart);
            var overlapEnd = Min(trimRange.End, segmentEnd);
            if (overlapEnd <= overlapStart)
            {
                continue;
            }

            result.Add(new TrimmedSegmentRange(
                index + 1,
                segment.SourcePath,
                segmentStart,
                overlapStart - segmentStart,
                overlapEnd - segmentStart,
                segment.Duration.Value));
        }

        return result;
    }

    private static TimeSpan Max(TimeSpan left, TimeSpan right)
    {
        return left >= right ? left : right;
    }

    private static TimeSpan Min(TimeSpan left, TimeSpan right)
    {
        return left <= right ? left : right;
    }

    private ConversionResult BuildTrimFailureResult(
        string inputPath,
        string outputPath,
        OutputFormat outputFormat,
        FpsOption fps,
        TrimRange trimRange,
        string userMessage,
        string technicalDetails = "",
        string conversionMode = "Fast")
    {
        return new ConversionResult(
            false,
            userMessage,
            ffmpegTools.FfmpegPath,
            Array.Empty<string>(),
            inputPath,
            outputPath,
            fps,
            null,
            "",
            string.IsNullOrWhiteSpace(technicalDetails) ? userMessage : technicalDetails,
            ConversionMode: conversionMode,
            OutputFormat: outputFormat.DisplayName(),
            Duration: trimRange.End - trimRange.Start,
            UsedDeterminateProgress: true,
            InputPathMode: ConversionInputPathMode.TrimmedCleanH264);
    }

    private static string BuildTrimTechnicalNote(TrimRange trimRange, DatPreviewWindowResult extractionResult)
    {
        var alignmentNote = extractionResult.SelectedKeyframeLocalOffset.HasValue &&
                            extractionResult.SelectedKeyframeLocalOffset.Value < trimRange.Start
            ? $"Fast trim start was aligned to the nearest usable H264 keyframe at {FormatDuration(extractionResult.SelectedKeyframeLocalOffset.Value)}."
            : "Fast trim start used a usable H264 keyframe at the selected Start.";

        return string.Join(
            Environment.NewLine,
            [
                "Trimmed Fast conversion used DAT frame-record extraction.",
                $"Selected trim: {FormatDuration(trimRange.Start)} to {FormatDuration(trimRange.End)}.",
                alignmentNote,
                extractionResult.TechnicalDetails.Trim()
            ]);
    }

    private static string BuildTrimEncodeTechnicalNote(TrimRange trimRange, TimeSpan preRoll, DatPreviewWindowResult extractionResult)
    {
        return string.Join(
            Environment.NewLine,
            [
                "Trimmed Full conversion used DAT frame-record extraction before FFmpeg re-encode.",
                $"Selected trim: {FormatDuration(trimRange.Start)} to {FormatDuration(trimRange.End)}.",
                $"FFmpeg pre-roll skip: {FormatDuration(preRoll)}.",
                extractionResult.TechnicalDetails.Trim()
            ]);
    }

    private static string BuildSplitTrimEncodeTechnicalNote(TrimRange trimRange, TimeSpan preRoll, IReadOnlyList<string> extractionDetails)
    {
        return string.Join(
            Environment.NewLine,
            [
                "Trimmed split Full conversion used DAT frame-record extraction before FFmpeg re-encode.",
                $"Selected trim: {FormatDuration(trimRange.Start)} to {FormatDuration(trimRange.End)}.",
                $"FFmpeg pre-roll skip: {FormatDuration(preRoll)}.",
                "Segment trim extraction details:",
                string.Join(Environment.NewLine, extractionDetails.Select(details => details.Trim()))
            ]);
    }

    private static TimeSpan CalculatePreRoll(TimeSpan selectedStart, TimeSpan? extractedKeyframeStart)
    {
        if (!extractedKeyframeStart.HasValue || extractedKeyframeStart.Value >= selectedStart)
        {
            return TimeSpan.Zero;
        }

        return selectedStart - extractedKeyframeStart.Value;
    }

    private static string BuildSplitTrimTechnicalNote(TrimRange trimRange, IReadOnlyList<string> extractionDetails)
    {
        return string.Join(
            Environment.NewLine,
            [
                "Trimmed split Fast conversion used DAT frame-record extraction.",
                $"Selected trim: {FormatDuration(trimRange.Start)} to {FormatDuration(trimRange.End)}.",
                "Segment trim extraction details:",
                string.Join(Environment.NewLine, extractionDetails.Select(details => details.Trim()))
            ]);
    }

    private static string FormatDuration(TimeSpan duration)
    {
        return duration.ToString(@"hh\:mm\:ss\.fff", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
        }
    }

    private async Task<(int Width, int Height)> ProbeH264DimensionsAsync(string h264Path, FpsOption fps, CancellationToken cancellationToken)
    {
        var arguments = new[]
        {
            "-v", "error",
            "-f", "h264",
            "-framerate", fps.FfmpegValue,
            "-i", h264Path,
            "-select_streams", "v:0",
            "-show_entries", "stream=width,height",
            "-of", "json"
        };

        var result = await runProcessAsync(ffmpegTools.FfprobePath, arguments, TimeSpan.FromSeconds(8), cancellationToken, null, null);
        if (result.ExitCode == 0)
        {
            try
            {
                using var document = JsonDocument.Parse(result.StandardOutput);
                var streams = document.RootElement.GetProperty("streams");
                if (streams.GetArrayLength() > 0)
                {
                    var stream = streams[0];
                    var width = stream.TryGetProperty("width", out var widthProperty) && widthProperty.TryGetInt32(out var parsedWidth)
                        ? parsedWidth
                        : 0;
                    var height = stream.TryGetProperty("height", out var heightProperty) && heightProperty.TryGetInt32(out var parsedHeight)
                        ? parsedHeight
                        : 0;
                    if (width > 0 && height > 0)
                    {
                        return (MakeEven(width), MakeEven(height));
                    }
                }
            }
            catch (JsonException)
            {
            }
        }

        return (1920, 1080);
    }

    private static FpsOption ResolveStoryboardSegmentSourceFps(StoryboardTrimSegment segment, FpsOption finalOutputFps, List<string> technicalDetails)
    {
        if (!string.IsNullOrWhiteSpace(segment.SourceFfmpegFpsValue))
        {
            var label = string.IsNullOrWhiteSpace(segment.SourceFpsLabel)
                ? segment.SourceFfmpegFpsValue
                : segment.SourceFpsLabel;
            technicalDetails.Add($"Segment {segment.OutputSegmentNumber} source FPS: using detected storyboard clip FPS {label} ({segment.SourceFfmpegFpsValue}). Reason: {FormatLogValue(segment.SourceFpsDecisionReason)}");
            return new FpsOption(label, segment.SourceFfmpegFpsValue);
        }

        try
        {
            var sidecarPath = SpotterSidecarLookup.FindSidecarForDat(segment.SourceDatPath);
            var detection = new SpotterFpsDetector().Detect(segment.SourceDatPath, sidecarPath);
            var decision = new FpsDecisionPolicy().Decide(detection);
            if (decision.ShouldUseDetectedRate && !string.IsNullOrWhiteSpace(decision.FfmpegRateValue))
            {
                technicalDetails.Add($"Segment {segment.OutputSegmentNumber} source FPS: detected during storyboard merge as {decision.UserFacingLabel} ({decision.FfmpegRateValue}). Reason: {FormatLogValue(decision.DecisionReason)}");
                return new FpsOption(decision.UserFacingLabel, decision.FfmpegRateValue);
            }

            technicalDetails.Add($"Segment {segment.OutputSegmentNumber} source FPS: detection was unavailable or uncertain; using final output FPS fallback {finalOutputFps.Label} ({finalOutputFps.FfmpegValue}). Reason: {FormatLogValue(decision.DecisionReason)}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            technicalDetails.Add($"Segment {segment.OutputSegmentNumber} source FPS: detection failed; using final output FPS fallback {finalOutputFps.Label} ({finalOutputFps.FfmpegValue}). Error: {ex.Message}");
        }

        return finalOutputFps;
    }

    private async Task<NormalizedStoryboardSegmentValidationResult> ValidateNormalizedStoryboardSegmentsAsync(
        IReadOnlyList<string> segmentPaths,
        IReadOnlyList<StoryboardTrimSegment> segments,
        CancellationToken cancellationToken)
    {
        var results = new List<NormalizedStoryboardSegmentValidationItem>();
        for (var index = 0; index < segmentPaths.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = segmentPaths[index];
            var segment = index < segments.Count ? segments[index] : null;
            var bytes = TryGetFileLength(path);

            if (!File.Exists(path))
            {
                results.Add(CreateSegmentValidationItem(index, segment, path, bytes, false, null, null, null, null, null, "failed: file does not exist"));
                continue;
            }

            if (bytes <= 0)
            {
                results.Add(CreateSegmentValidationItem(index, segment, path, bytes, false, null, null, null, null, null, "failed: file is empty"));
                continue;
            }

            var arguments = new[]
            {
                "-v", "error",
                "-i", path,
                "-select_streams", "v:0",
                "-show_entries", "stream=codec_name,width,height,r_frame_rate,avg_frame_rate,duration",
                "-show_entries", "format=duration",
                "-of", "json"
            };

            var probeResult = await runProcessAsync(ffmpegTools.FfprobePath, arguments, TimeSpan.FromSeconds(8), cancellationToken, null, null);
            var parsed = probeResult.ExitCode == 0
                ? TryParseNormalizedStoryboardSegmentProbe(probeResult.StandardOutput, probeResult.StandardError)
                : NormalizedStoryboardSegmentProbeResult.Failure($"ffprobe exit code {FormatExitCode(probeResult.ExitCode)}; stderr={FormatProcessTextForLog(probeResult.StandardError)}");

            results.Add(CreateSegmentValidationItem(
                index,
                segment,
                path,
                bytes,
                parsed.VideoStreamFound,
                parsed.CodecName,
                parsed.Width,
                parsed.Height,
                parsed.Duration,
                parsed.FrameRate,
                parsed.ValidationResult));
        }

        var firstFailure = results.FirstOrDefault(result => !result.IsValid);
        return firstFailure is null
            ? NormalizedStoryboardSegmentValidationResult.Valid(results)
            : NormalizedStoryboardSegmentValidationResult.Invalid(results, $"Segment {firstFailure.SegmentIndex} did not contain a valid video stream.");
    }

    private static NormalizedStoryboardSegmentValidationItem CreateSegmentValidationItem(
        int zeroBasedIndex,
        StoryboardTrimSegment? segment,
        string path,
        long bytes,
        bool videoStreamFound,
        string? codecName,
        int? width,
        int? height,
        string? duration,
        string? frameRate,
        string validationResult)
    {
        return new NormalizedStoryboardSegmentValidationItem(
            zeroBasedIndex + 1,
            segment?.ClipNumber,
            segment?.CameraDisplayName ?? segment?.ClipName,
            path,
            bytes,
            videoStreamFound,
            codecName,
            width,
            height,
            duration,
            frameRate,
            validationResult);
    }

    private static NormalizedStoryboardSegmentProbeResult TryParseNormalizedStoryboardSegmentProbe(string json, string standardError)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!root.TryGetProperty("streams", out var streams) ||
                streams.ValueKind != JsonValueKind.Array ||
                streams.GetArrayLength() == 0)
            {
                return NormalizedStoryboardSegmentProbeResult.Failure("failed: ffprobe did not report a video stream");
            }

            var stream = streams[0];
            var codecName = GetJsonString(stream, "codec_name");
            var width = GetJsonInt(stream, "width");
            var height = GetJsonInt(stream, "height");
            var streamDuration = GetJsonString(stream, "duration");
            var formatDuration = root.TryGetProperty("format", out var format) ? GetJsonString(format, "duration") : null;
            var avgFrameRate = GetJsonString(stream, "avg_frame_rate");
            var rFrameRate = GetJsonString(stream, "r_frame_rate");

            if (string.IsNullOrWhiteSpace(codecName))
            {
                return NormalizedStoryboardSegmentProbeResult.Failure("failed: ffprobe did not report a video codec");
            }

            if ((width.HasValue && width.Value <= 0) || (height.HasValue && height.Value <= 0))
            {
                return NormalizedStoryboardSegmentProbeResult.Failure("failed: ffprobe reported an invalid video resolution", codecName, width, height, FirstUsefulLogValue(formatDuration, streamDuration), FirstUsefulLogValue(avgFrameRate, rFrameRate));
            }

            return NormalizedStoryboardSegmentProbeResult.Success(
                codecName,
                width,
                height,
                FirstUsefulLogValue(formatDuration, streamDuration),
                FirstUsefulLogValue(avgFrameRate, rFrameRate),
                string.IsNullOrWhiteSpace(standardError) ? "passed" : $"passed; ffprobe stderr={standardError.Trim()}");
        }
        catch (JsonException ex)
        {
            return NormalizedStoryboardSegmentProbeResult.Failure($"failed: could not parse ffprobe JSON output: {ex.Message}");
        }
    }

    private static string BuildNormalizedStoryboardSegmentValidationLog(NormalizedStoryboardSegmentValidationResult validation, TimeSpan elapsed)
    {
        var lines = new List<string>
        {
            "Normalized storyboard segment validation",
            $"Elapsed: {elapsed}",
            $"Overall result: {(validation.IsValid ? "passed" : "failed")}",
            $"Message: {validation.Message}"
        };

        foreach (var segment in validation.Segments)
        {
            lines.Add(
                $"- Segment {segment.SegmentIndex}: source_clip_index={FormatNullableInt(segment.SourceClipIndex)}; camera={FormatLogValue(segment.CameraDisplayName)}; path={segment.Path}; bytes={segment.Bytes}; video_stream_found={FormatYesNo(segment.VideoStreamFound)}; codec={FormatLogValue(segment.CodecName)}; resolution={FormatResolutionForLog(segment.Width, segment.Height)}; duration={FormatLogValue(segment.Duration)}; frame_rate={FormatLogValue(segment.FrameRate)}; validation_result={segment.ValidationResult}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private ConversionResult BuildStoryboardFailureResult(
        string inputPath,
        string outputPath,
        OutputFormat outputFormat,
        FpsOption fps,
        TimeSpan? duration,
        string userMessage,
        IReadOnlyList<string> technicalDetails,
        TimeSpan processingTime)
    {
        PartialOutputService.TryMovePartialOutput(outputPath, inputPath);
        return new ConversionResult(
            false,
            userMessage,
            ffmpegTools.FfmpegPath,
            Array.Empty<string>(),
            inputPath,
            outputPath,
            fps,
            null,
            "",
            string.Join(Environment.NewLine, technicalDetails),
            ConversionMode: ConversionModes.Encode,
            OutputFormat: outputFormat.DisplayName(),
            Duration: duration,
            UsedDeterminateProgress: duration.HasValue,
            ProcessingTime: processingTime,
            InputPathMode: ConversionInputPathMode.StoryboardCombinedCleanH264);
    }

    private static int MakeEven(int value)
    {
        return value % 2 == 0 ? value : value - 1;
    }

    private static string EscapeConcatPath(string path)
    {
        return Path.GetFullPath(path).Replace("\\", "/", StringComparison.Ordinal).Replace("'", "'\\''", StringComparison.Ordinal);
    }

    public static void WriteStoryboardConcatList(string concatListPath, IEnumerable<string> clipPaths)
    {
        var lines = clipPaths.Select(path => $"file '{EscapeConcatPath(path)}'");
        File.WriteAllLines(concatListPath, lines, FfmpegConcatListEncoding);
    }

    public static StoryboardConcatListValidationResult ValidateStoryboardConcatList(string concatListPath, IReadOnlyList<string> expectedClipPaths)
    {
        if (!File.Exists(concatListPath))
        {
            return StoryboardConcatListValidationResult.Invalid($"Concat list does not exist: {concatListPath}");
        }

        var bytes = File.ReadAllBytes(concatListPath);
        if (bytes.Length == 0)
        {
            return StoryboardConcatListValidationResult.Invalid($"Concat list is empty: {concatListPath}");
        }

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return StoryboardConcatListValidationResult.Invalid("Concat list starts with a UTF-8 BOM.");
        }

        var lines = File.ReadAllLines(concatListPath, FfmpegConcatListEncoding);
        var nonEmptyLines = lines.Where(line => !string.IsNullOrWhiteSpace(line)).ToList();
        var firstNonEmptyLine = nonEmptyLines.FirstOrDefault();
        if (firstNonEmptyLine is null)
        {
            return StoryboardConcatListValidationResult.Invalid("Concat list does not contain any file entries.");
        }

        if (!firstNonEmptyLine.StartsWith("file ", StringComparison.Ordinal))
        {
            return StoryboardConcatListValidationResult.Invalid($"Concat list first entry is invalid: {firstNonEmptyLine}");
        }

        if (nonEmptyLines.Count != expectedClipPaths.Count)
        {
            return StoryboardConcatListValidationResult.Invalid($"Concat list entry count {nonEmptyLines.Count} does not match normalized clip count {expectedClipPaths.Count}.");
        }

        for (var index = 0; index < expectedClipPaths.Count; index++)
        {
            var clipPath = expectedClipPaths[index];
            var expectedLine = $"file '{EscapeConcatPath(clipPath)}'";
            if (!string.Equals(nonEmptyLines[index], expectedLine, StringComparison.Ordinal))
            {
                return StoryboardConcatListValidationResult.Invalid($"Concat list entry {index + 1} does not match expected normalized clip path.");
            }

            if (!File.Exists(clipPath))
            {
                return StoryboardConcatListValidationResult.Invalid($"Referenced normalized clip does not exist: {clipPath}");
            }

            if (TryGetFileLength(clipPath) <= 0)
            {
                return StoryboardConcatListValidationResult.Invalid($"Referenced normalized clip is empty: {clipPath}");
            }
        }

        return StoryboardConcatListValidationResult.Valid($"Concat list contains {expectedClipPaths.Count} normalized clip entries.");
    }

    private static string FormatExitCode(int? exitCode)
    {
        return exitCode?.ToString(CultureInfo.InvariantCulture) ?? "none";
    }

    private static string FormatProcessTextForLog(string text)
    {
        return string.IsNullOrWhiteSpace(text) ? "(none)" : text.Trim();
    }

    private static string? GetJsonString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind != JsonValueKind.Null
            ? property.ToString()
            : null;
    }

    private static int? GetJsonInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.TryGetInt32(out var value) ? value : null;
    }

    private static string? FirstUsefulLogValue(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value) && value != "N/A")
            {
                return value;
            }
        }

        return null;
    }

    private static string FormatNullableInt(int? value)
    {
        return value?.ToString(CultureInfo.InvariantCulture) ?? "unknown";
    }

    private static string FormatLogValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "unknown" : value;
    }

    private static string FormatResolutionForLog(int? width, int? height)
    {
        return width.HasValue && height.HasValue ? $"{width.Value}x{height.Value}" : "unknown";
    }

    private static string FormatYesNo(bool value)
    {
        return value ? "yes" : "no";
    }

    private static string QuoteArgumentForLog(string value)
    {
        return value.Contains(' ') || value.Contains(';') || value.Contains('[') || value.Contains(']')
            ? $"\"{value}\""
            : value;
    }

    private static string FormatDurationForLog(TimeSpan value)
    {
        return value.ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture);
    }

    private static string PrependTechnicalNote(string existingText, string note)
    {
        return string.IsNullOrWhiteSpace(existingText)
            ? note
            : $"{note}{Environment.NewLine}{existingText}";
    }

    private ConversionResult BuildUnresolvedFpsResult(
        string inputPath,
        string outputPath,
        OutputFormat outputFormat,
        FpsOption fps,
        string conversionMode,
        TimeSpan? duration)
    {
        return new ConversionResult(
            false,
            "Conversion blocked because Source FPS is not set.",
            ffmpegTools.FfmpegPath,
            Array.Empty<string>(),
            inputPath,
            outputPath,
            fps,
            null,
            "",
            "Source FPS is not set. Choose Source FPS before converting.",
            ConversionMode: conversionMode,
            OutputFormat: outputFormat.DisplayName(),
            Duration: duration,
            UsedDeterminateProgress: duration.HasValue);
    }

    private sealed record TrimmedSegmentRange(
        int SegmentIndex,
        string SourcePath,
        TimeSpan GlobalStart,
        TimeSpan LocalStart,
        TimeSpan LocalEnd,
        TimeSpan Duration);

    private sealed record NormalizedStoryboardSegmentProbeResult(
        bool VideoStreamFound,
        string? CodecName,
        int? Width,
        int? Height,
        string? Duration,
        string? FrameRate,
        string ValidationResult)
    {
        public static NormalizedStoryboardSegmentProbeResult Success(
            string codecName,
            int? width,
            int? height,
            string? duration,
            string? frameRate,
            string validationResult)
        {
            return new NormalizedStoryboardSegmentProbeResult(true, codecName, width, height, duration, frameRate, validationResult);
        }

        public static NormalizedStoryboardSegmentProbeResult Failure(
            string validationResult,
            string? codecName = null,
            int? width = null,
            int? height = null,
            string? duration = null,
            string? frameRate = null)
        {
            return new NormalizedStoryboardSegmentProbeResult(false, codecName, width, height, duration, frameRate, validationResult);
        }
    }
}

public sealed record StoryboardConcatListValidationResult(bool IsValid, string Message)
{
    public static StoryboardConcatListValidationResult Valid(string message)
    {
        return new StoryboardConcatListValidationResult(true, message);
    }

    public static StoryboardConcatListValidationResult Invalid(string message)
    {
        return new StoryboardConcatListValidationResult(false, message);
    }
}

public sealed record NormalizedStoryboardSegmentValidationResult(
    bool IsValid,
    string Message,
    IReadOnlyList<NormalizedStoryboardSegmentValidationItem> Segments)
{
    public static NormalizedStoryboardSegmentValidationResult Valid(IReadOnlyList<NormalizedStoryboardSegmentValidationItem> segments)
    {
        return new NormalizedStoryboardSegmentValidationResult(true, $"Validated {segments.Count} normalized storyboard segment(s).", segments);
    }

    public static NormalizedStoryboardSegmentValidationResult Invalid(IReadOnlyList<NormalizedStoryboardSegmentValidationItem> segments, string message)
    {
        return new NormalizedStoryboardSegmentValidationResult(false, message, segments);
    }
}

public sealed record NormalizedStoryboardSegmentValidationItem(
    int SegmentIndex,
    int? SourceClipIndex,
    string? CameraDisplayName,
    string Path,
    long Bytes,
    bool VideoStreamFound,
    string? CodecName,
    int? Width,
    int? Height,
    string? Duration,
    string? FrameRate,
    string ValidationResult)
{
    public bool IsValid => Bytes > 0 && VideoStreamFound;
}
