namespace DatConverter;

public sealed class QueueItemFpsResolver
{
    private readonly Func<string, string?, MirasysFpsDetectionResult> detect;
    private readonly Func<MirasysFpsDetectionResult, FpsDecisionResult> decide;
    private readonly Func<string, string?> findSidecar;

    public QueueItemFpsResolver()
        : this(
            (datPath, sidecarPath) => new MirasysFpsDetector().Detect(datPath, sidecarPath),
            detection => new FpsDecisionPolicy().Decide(detection),
            MirasysSidecarLookup.FindSidecarForDat)
    {
    }

    public QueueItemFpsResolver(
        Func<string, string?, MirasysFpsDetectionResult> detect,
        Func<MirasysFpsDetectionResult, FpsDecisionResult> decide,
        Func<string, string?>? findSidecar = null)
    {
        this.detect = detect;
        this.decide = decide;
        this.findSidecar = findSidecar ?? (_ => null);
    }

    public QueueItemFpsResolution ResolveQueueItemFps(string datPath, QueueItemFpsSettings settings)
    {
        if (settings.SelectionMode == FpsSelectionMode.Manual)
        {
            return QueueItemFpsResolution.FromManual(settings.ToManualFpsOption());
        }

        var sidecarPath = findSidecar(datPath);
        var detection = detect(datPath, sidecarPath);
        var decision = decide(detection);
        if (!decision.ShouldUseDetectedRate)
        {
            var warning = BuildFallbackWarning(detection, decision);
            return new QueueItemFpsResolution
            {
                SelectionMode = FpsSelectionMode.AutoDetect,
                DisplayLabel = "Needs manual selection",
                FfmpegRateValue = string.Empty,
                NominalConversionFps = null,
                HasResolvedFps = false,
                RequiresManualFpsSelection = true,
                FpsValidationMessage = "Auto-detect could not determine the source FPS. Double-click this row and choose Source FPS.",
                AutoDetectionSucceeded = detection.Succeeded,
                Confidence = "Unavailable",
                Warning = warning ?? "FPS auto-detection was unavailable or uncertain.",
                DecisionReason = decision.DecisionReason,
                TechnicalLogText = decision.TechnicalLogText
            };
        }

        return new QueueItemFpsResolution
        {
            SelectionMode = FpsSelectionMode.AutoDetect,
            DisplayLabel = decision.UserFacingLabel.Replace(" fps", "", StringComparison.OrdinalIgnoreCase),
            FfmpegRateValue = decision.FfmpegRateValue,
            NominalConversionFps = decision.NominalConversionFps,
            HasResolvedFps = true,
            RequiresManualFpsSelection = false,
            AutoDetectionSucceeded = decision.AutoDetectionSucceeded,
            Confidence = decision.Confidence,
            DecisionReason = "Detected from Mirasys frame records.",
            TechnicalLogText = decision.TechnicalLogText
        };
    }

    private static string? BuildFallbackWarning(MirasysFpsDetectionResult detection, FpsDecisionResult decision)
    {
        if (!string.IsNullOrWhiteSpace(detection.FailureReason))
        {
            return $"FPS auto-detection failed: {detection.FailureReason}";
        }

        return decision.Warnings.FirstOrDefault();
    }
}
