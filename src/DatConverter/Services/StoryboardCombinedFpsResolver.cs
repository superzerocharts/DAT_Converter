using System.Globalization;

namespace DatConverter;

public sealed class StoryboardCombinedFpsResolver
{
    private readonly Func<string, string?, SpotterFpsDetectionResult> detect;
    private readonly Func<SpotterFpsDetectionResult, FpsDecisionResult> decide;
    private readonly Func<string, string?> findSidecar;

    public StoryboardCombinedFpsResolver()
        : this(
            (datPath, sidecarPath) => new SpotterFpsDetector().Detect(datPath, sidecarPath),
            detection => new FpsDecisionPolicy().Decide(detection),
            SpotterSidecarLookup.FindSidecarForDat)
    {
    }

    public StoryboardCombinedFpsResolver(
        Func<string, string?, SpotterFpsDetectionResult> detect,
        Func<SpotterFpsDetectionResult, FpsDecisionResult> decide,
        Func<string, string?>? findSidecar = null)
    {
        this.detect = detect;
        this.decide = decide;
        this.findSidecar = findSidecar ?? (_ => null);
    }

    public QueueItemFpsResolution ResolveCombinedOutputFps(SpotterStoryboardPlan plan, QueueItemFpsSettings settings)
    {
        if (settings.SelectionMode == FpsSelectionMode.Manual)
        {
            var manual = QueueItemFpsResolution.FromManual(settings.ToManualFpsOption());
            return new QueueItemFpsResolution
            {
                SelectionMode = manual.SelectionMode,
                DisplayLabel = manual.DisplayLabel,
                FfmpegRateValue = manual.FfmpegRateValue,
                NominalConversionFps = manual.NominalConversionFps,
                HasResolvedFps = manual.HasResolvedFps,
                RequiresManualFpsSelection = manual.RequiresManualFpsSelection,
                FpsValidationMessage = manual.FpsValidationMessage,
                AutoDetectionSucceeded = manual.AutoDetectionSucceeded,
                Confidence = manual.Confidence,
                Warning = manual.Warning,
                DecisionReason = "Manual FPS selection for combined Storyboard output.",
                TechnicalLogText = BuildManualTechnicalLog(plan, manual)
            };
        }

        var clipEvidence = DetectClipFpsEvidence(plan);
        return new QueueItemFpsResolution
        {
            SelectionMode = FpsSelectionMode.AutoDetect,
            DisplayLabel = "Auto 30",
            FfmpegRateValue = "30",
            NominalConversionFps = 30,
            HasResolvedFps = true,
            RequiresManualFpsSelection = false,
            AutoDetectionSucceeded = clipEvidence.Any(evidence => evidence.Decision.AutoDetectionSucceeded),
            Confidence = "Medium",
            DecisionReason = "Combined Storyboard output uses a constant 30 fps when Source FPS is Auto-detect.",
            TechnicalLogText = BuildAutoTechnicalLog(plan, clipEvidence)
        };
    }

    private IReadOnlyList<ClipFpsEvidence> DetectClipFpsEvidence(SpotterStoryboardPlan plan)
    {
        var evidence = new List<ClipFpsEvidence>();
        foreach (var clip in plan.Clips.OrderBy(clip => clip.ClipNumber))
        {
            var sidecarPath = findSidecar(clip.DatFilePath);
            var detection = detect(clip.DatFilePath, sidecarPath);
            var decision = decide(detection);
            if (decision.ShouldUseDetectedRate && !string.IsNullOrWhiteSpace(decision.FfmpegRateValue))
            {
                clip.SourceFpsLabel = decision.UserFacingLabel;
                clip.SourceFfmpegFpsValue = decision.FfmpegRateValue;
                clip.SourceFpsDecisionReason = decision.DecisionReason;
            }

            evidence.Add(new ClipFpsEvidence(clip, sidecarPath, detection, decision));
        }

        return evidence;
    }

    private static string BuildAutoTechnicalLog(SpotterStoryboardPlan plan, IReadOnlyList<ClipFpsEvidence> clipEvidence)
    {
        var lines = new List<string>
        {
            "Combined Storyboard FPS policy",
            "Source FPS selection: Auto-detect",
            "Final combined output FPS: Auto 30",
            "Final combined FFmpeg FPS value: 30",
            "Policy: combined Storyboard output is constant frame rate; per-clip detected FPS is preserved as evidence and does not force the final output FPS.",
            $"Storyboard clips: {plan.ClipCount}"
        };

        foreach (var evidence in clipEvidence)
        {
            lines.Add(FormatClipEvidence(evidence));
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildManualTechnicalLog(SpotterStoryboardPlan plan, QueueItemFpsResolution manual)
    {
        return string.Join(
            Environment.NewLine,
            "Combined Storyboard FPS policy",
            $"Source FPS selection: Manual {manual.DisplayLabel}",
            $"Final combined output FPS: {manual.DisplayLabel}",
            $"Final combined FFmpeg FPS value: {manual.FfmpegRateValue}",
            "Policy: combined Storyboard output is constant frame rate and uses the manually selected FPS.",
            $"Storyboard clips: {plan.ClipCount}");
    }

    private static string FormatClipEvidence(ClipFpsEvidence evidence)
    {
        var clip = evidence.Clip;
        var decision = evidence.Decision;
        var details = evidence.Detection.TechnicalDetails;
        var metadataFile = string.IsNullOrWhiteSpace(evidence.SidecarPath)
            ? "none"
            : Path.GetFileName(evidence.SidecarPath);

        return string.Format(
            CultureInfo.InvariantCulture,
            "Clip {0}: {1}; dat={2}; metadata file={3}; detected={4}; ffmpeg={5}; confidence={6}; average={7}; bucket_median={8}; bucket_mode={9}; frames={10}; reason={11}",
            clip.ClipNumber,
            string.IsNullOrWhiteSpace(clip.CameraDisplayName) ? clip.ClipName : clip.CameraDisplayName,
            clip.DatFileName,
            metadataFile,
            decision.ShouldUseDetectedRate ? decision.UserFacingLabel : "unavailable",
            decision.ShouldUseDetectedRate ? decision.FfmpegRateValue : "not set",
            decision.Confidence,
            FormatNullable(details.AverageFps),
            FormatNullable(details.BucketMedianFps),
            details.BucketModeFps?.ToString(CultureInfo.InvariantCulture) ?? "unknown",
            details.FrameCount,
            string.IsNullOrWhiteSpace(decision.DecisionReason) ? "none" : decision.DecisionReason);
    }

    private static string FormatNullable(double? value)
    {
        return value.HasValue
            ? value.Value.ToString("0.###", CultureInfo.InvariantCulture)
            : "unknown";
    }

    private sealed record ClipFpsEvidence(
        SpotterStoryboardClip Clip,
        string? SidecarPath,
        SpotterFpsDetectionResult Detection,
        FpsDecisionResult Decision);
}
