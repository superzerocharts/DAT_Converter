using System.Diagnostics;

namespace DatConverter;

public sealed class ConversionService
{
    private static readonly TimeSpan ConversionTimeout = TimeSpan.FromHours(12);

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

        if (processResult.ExitCode == 0 && TryGetFileLength(outputPath) > 0)
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
                InputPathMode: inputPathMode);
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
            inputPathMode);
    }

    private static string ResolveConversionFailureMessage(IReadOnlyList<string> arguments, string failureMessage)
    {
        return UsesBurnTimestampFilter(arguments)
            ? BurnTimestampMetadataBuilder.BundledFfmpegUnavailableMessage
            : failureMessage;
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
}
