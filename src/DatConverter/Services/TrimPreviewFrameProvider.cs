using System.Globalization;

namespace DatConverter;

public sealed class TrimPreviewFrameProvider : IDisposable
{
    private static readonly TimeSpan PreviewTimeout = TimeSpan.FromSeconds(20);

    private readonly FfmpegTools ffmpegTools;
    private readonly Func<string, IReadOnlyList<string>, TimeSpan, CancellationToken, Task<ProcessRunResult>> runProcessAsync;
    private readonly DatPreviewWindowExtractor previewWindowExtractor;
    private readonly string tempDirectory;

    public TrimPreviewFrameProvider(FfmpegTools ffmpegTools)
        : this(ffmpegTools, (executablePath, arguments, timeout, cancellationToken) => FfmpegProcessRunner.RunAsync(executablePath, arguments, timeout, cancellationToken))
    {
    }

    public TrimPreviewFrameProvider(
        FfmpegTools ffmpegTools,
        Func<string, IReadOnlyList<string>, TimeSpan, CancellationToken, Task<ProcessRunResult>> runProcessAsync)
    {
        this.ffmpegTools = ffmpegTools;
        this.runProcessAsync = runProcessAsync;
        previewWindowExtractor = new DatPreviewWindowExtractor();
        tempDirectory = Path.Combine(Path.GetTempPath(), "DatConverterTrimPreview", Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(tempDirectory);
    }

    public string TempDirectory => tempDirectory;

    public string CleanupTechnicalDetails { get; private set; } = "Temp cleanup: not run";

    public static bool TryPlanFrameRequest(
        FfmpegTools ffmpegTools,
        RecordingTimeline timeline,
        TimeSpan timelineOffset,
        FpsOption fps,
        string previewInputPath,
        string outputImagePath,
        TimeSpan previewSeekOffset,
        TimeSpan? selectedKeyframeLocalOffset,
        out TrimPreviewFrameRequest? request)
    {
        request = null;
        if (string.IsNullOrWhiteSpace(ffmpegTools.FfmpegPath) || string.IsNullOrWhiteSpace(fps.FfmpegValue))
        {
            return false;
        }

        var sourcePath = timeline.SourcePath;
        var sourceLocalOffset = timelineOffset < TimeSpan.Zero ? TimeSpan.Zero : timelineOffset;

        if (timeline.IsSplitRecording)
        {
            if (!timeline.TryMapElapsedOffset(sourceLocalOffset, out var position) || position is null)
            {
                return false;
            }

            sourcePath = position.Segment.SourcePath;
            sourceLocalOffset = position.SegmentLocalOffset;
        }

        request = new TrimPreviewFrameRequest
        {
            FfmpegPath = ffmpegTools.FfmpegPath,
            SourcePath = sourcePath,
            PreviewInputPath = previewInputPath,
            OutputImagePath = outputImagePath,
            TimelineOffset = timelineOffset,
            SourceLocalOffset = sourceLocalOffset,
            PreviewSeekOffset = previewSeekOffset,
            SelectedKeyframeLocalOffset = selectedKeyframeLocalOffset,
            Arguments = BuildArguments(previewInputPath, previewSeekOffset, fps, outputImagePath)
        };
        return true;
    }

    public async Task<TrimPreviewFrameResult> ExtractFrameAsync(
        RecordingTimeline timeline,
        TimeSpan timelineOffset,
        FpsOption fps,
        CancellationToken cancellationToken)
    {
        var mappedSourcePath = timeline.SourcePath;
        var mappedLocalOffset = timelineOffset < TimeSpan.Zero ? TimeSpan.Zero : timelineOffset;

        if (timeline.IsSplitRecording)
        {
            if (!timeline.TryMapElapsedOffset(mappedLocalOffset, out var position) || position is null)
            {
                return new TrimPreviewFrameResult
                {
                    Succeeded = false,
                    TechnicalDetails = $"Preview frame request could not be mapped to a split recording segment.{Environment.NewLine}Timeline offset: {FormatOffset(timelineOffset)}"
                };
            }

            mappedSourcePath = position.Segment.SourcePath;
            mappedLocalOffset = position.SegmentLocalOffset;
        }

        var previewInputPath = Path.Combine(tempDirectory, $"preview-window-{Guid.NewGuid():N}.h264");
        var segmentDuration = ResolveMappedSegmentDuration(timeline, mappedSourcePath);
        var windowResult = await Task.Run(
            () => previewWindowExtractor.ExtractWindow(mappedSourcePath, mappedLocalOffset, segmentDuration, previewInputPath, cancellationToken),
            cancellationToken);
        if (!windowResult.Succeeded || string.IsNullOrWhiteSpace(windowResult.OutputPath))
        {
            return new TrimPreviewFrameResult
            {
                Succeeded = false,
                TechnicalDetails = BuildFailureDetails(timelineOffset, mappedSourcePath, mappedLocalOffset, windowResult, null, null)
            };
        }

        var outputImagePath = Path.Combine(tempDirectory, $"preview-{Guid.NewGuid():N}.jpg");
        if (!TryPlanFrameRequest(
                ffmpegTools,
                timeline,
                timelineOffset,
                fps,
                windowResult.OutputPath,
                outputImagePath,
                windowResult.PreviewSeekOffset,
                windowResult.SelectedKeyframeLocalOffset,
                out var request) || request is null)
        {
            return new TrimPreviewFrameResult
            {
                Succeeded = false,
                TechnicalDetails = "Preview frame request could not be planned."
            };
        }

        var result = await runProcessAsync(request.FfmpegPath, request.Arguments, PreviewTimeout, cancellationToken);
        var succeeded = result.ExitCode == 0 && File.Exists(outputImagePath);
        return new TrimPreviewFrameResult
        {
            Succeeded = succeeded,
            ImagePath = succeeded ? outputImagePath : null,
            TechnicalDetails = BuildTechnicalDetails(request, windowResult, result)
        };
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }

            CleanupTechnicalDetails = $"Temp cleanup: completed; path={tempDirectory}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CleanupTechnicalDetails = $"Temp cleanup: failed; path={tempDirectory}; reason={ex.Message}";
        }
    }

    private static IReadOnlyList<string> BuildArguments(
        string previewInputPath,
        TimeSpan previewSeekOffset,
        FpsOption fps,
        string outputImagePath)
    {
        return
        [
            "-y",
            "-hide_banner",
            "-loglevel",
            "warning",
            "-fflags",
            "+genpts+discardcorrupt",
            "-err_detect",
            "ignore_err",
            "-f",
            "h264",
            "-r",
            fps.FfmpegValue,
            "-i",
            previewInputPath,
            "-ss",
            FormatOffset(previewSeekOffset),
            "-frames:v",
            "1",
            "-q:v",
            "2",
            outputImagePath
        ];
    }

    private static TimeSpan? ResolveMappedSegmentDuration(RecordingTimeline timeline, string mappedSourcePath)
    {
        return timeline.Segments.FirstOrDefault(segment => string.Equals(segment.SourcePath, mappedSourcePath, StringComparison.OrdinalIgnoreCase))?.Duration
            ?? timeline.TotalDuration;
    }

    private static string BuildTechnicalDetails(TrimPreviewFrameRequest request, DatPreviewWindowResult windowResult, ProcessRunResult result)
    {
        return string.Join(
            Environment.NewLine,
            [
                $"Source DAT: {request.SourcePath}",
                $"Preview input: {request.PreviewInputPath}",
                $"Timeline offset: {FormatOffset(request.TimelineOffset)}",
                $"Source local offset: {FormatOffset(request.SourceLocalOffset)}",
                $"Selected keyframe local offset: {(request.SelectedKeyframeLocalOffset.HasValue ? FormatOffset(request.SelectedKeyframeLocalOffset.Value) : "none")}",
                $"Preview seek offset: {FormatOffset(request.PreviewSeekOffset)}",
                $"Window extraction: {(windowResult.Succeeded ? "Succeeded" : "Failed")}",
                $"Window records: {windowResult.WrittenFrameRecordCount}",
                $"FFmpeg command: {request.FfmpegPath} {string.Join(" ", request.Arguments.Select(QuoteArgument))}",
                $"Exit code: {result.ExitCode?.ToString(CultureInfo.InvariantCulture) ?? "none"}",
                $"Timed out: {FormatYesNo(result.TimedOut)}",
                $"Canceled: {FormatYesNo(result.WasCanceled)}",
                $"stderr: {(string.IsNullOrWhiteSpace(result.StandardError) ? "(none)" : result.StandardError.Trim())}"
            ]);
    }

    private static string BuildFailureDetails(
        TimeSpan timelineOffset,
        string mappedSourcePath,
        TimeSpan mappedLocalOffset,
        DatPreviewWindowResult? windowResult,
        TrimPreviewFrameRequest? request,
        ProcessRunResult? processResult)
    {
        return string.Join(
            Environment.NewLine,
            [
                $"Source DAT: {mappedSourcePath}",
                $"Timeline offset: {FormatOffset(timelineOffset)}",
                $"Source local offset: {FormatOffset(mappedLocalOffset)}",
                $"Selected keyframe local offset: {(windowResult?.SelectedKeyframeLocalOffset.HasValue == true ? FormatOffset(windowResult.SelectedKeyframeLocalOffset.Value) : "none")}",
                $"Preview window: {(windowResult?.Succeeded == true ? "Succeeded" : "Failed")}",
                $"Preview window failure: {windowResult?.FailureReason ?? "none"}",
                $"FFmpeg command: {(request is null ? "none" : request.FfmpegPath + " " + string.Join(" ", request.Arguments.Select(QuoteArgument)))}",
                $"FFmpeg exit code: {processResult?.ExitCode?.ToString(CultureInfo.InvariantCulture) ?? "none"}",
                $"FFmpeg stderr: {(string.IsNullOrWhiteSpace(processResult?.StandardError) ? "(none)" : processResult!.StandardError.Trim())}",
                $"Window details:{Environment.NewLine}{windowResult?.TechnicalDetails ?? "(none)"}"
            ]);
    }

    private static string QuoteArgument(string argument)
    {
        return argument.Contains(' ', StringComparison.Ordinal) ? $"\"{argument}\"" : argument;
    }

    private static string FormatOffset(TimeSpan offset)
    {
        return offset.ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture);
    }

    private static string FormatYesNo(bool value)
    {
        return value ? "Yes" : "No";
    }
}
