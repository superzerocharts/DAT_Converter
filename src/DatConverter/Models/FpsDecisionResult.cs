namespace DatConverter;

public sealed class FpsDecisionResult
{
    public bool AutoDetectionSucceeded { get; init; }
    public bool ShouldUseDetectedRate { get; init; }
    public double? RawAverageFps { get; init; }
    public double? RawBucketMedianFps { get; init; }
    public double? NominalConversionFps { get; init; }
    public string FfmpegRateValue { get; init; } = "30";
    public string UserFacingLabel { get; init; } = "30 fps";
    public string Confidence { get; init; } = "Low";
    public string DecisionReason { get; init; } = "";
    public string TechnicalLogText { get; init; } = "";
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}
