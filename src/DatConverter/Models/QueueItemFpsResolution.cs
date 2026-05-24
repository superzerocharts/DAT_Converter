namespace DatConverter;

public sealed class QueueItemFpsResolution
{
    public FpsSelectionMode SelectionMode { get; init; } = FpsSelectionMode.Manual;
    public string DisplayLabel { get; init; } = "30 fps";
    public string FfmpegRateValue { get; init; } = "30";
    public double? NominalConversionFps { get; init; } = 30;
    public bool HasResolvedFps { get; init; } = true;
    public bool RequiresManualFpsSelection { get; init; }
    public string? FpsValidationMessage { get; init; }
    public bool AutoDetectionSucceeded { get; init; }
    public string Confidence { get; init; } = "Manual";
    public string? Warning { get; init; }
    public string? DecisionReason { get; init; }
    public string? TechnicalLogText { get; init; }

    public FpsOption ToFpsOption()
    {
        if (!HasResolvedFps)
        {
            return new FpsOption("Needs FPS", string.Empty);
        }

        return new FpsOption(ToFpsOptionLabel(), FfmpegRateValue);
    }

    public static QueueItemFpsResolution FromManual(FpsOption fps)
    {
        return new QueueItemFpsResolution
        {
            SelectionMode = FpsSelectionMode.Manual,
            DisplayLabel = fps.Label,
            FfmpegRateValue = fps.FfmpegValue,
            NominalConversionFps = ParseNominalFps(fps.Label),
            HasResolvedFps = true,
            RequiresManualFpsSelection = false,
            AutoDetectionSucceeded = false,
            Confidence = "Manual",
            DecisionReason = "Manual FPS selection."
        };
    }

    public static QueueItemFpsResolution PendingAutoDetect()
    {
        return new QueueItemFpsResolution
        {
            SelectionMode = FpsSelectionMode.AutoDetect,
            DisplayLabel = "Auto-detecting",
            FfmpegRateValue = string.Empty,
            NominalConversionFps = null,
            HasResolvedFps = false,
            RequiresManualFpsSelection = true,
            FpsValidationMessage = "Detecting source frame rate...",
            Confidence = "Pending",
            DecisionReason = "Detecting source frame rate."
        };
    }

    private string ToFpsOptionLabel()
    {
        return SelectionMode == FpsSelectionMode.AutoDetect
            ? FormatNominalFps(NominalConversionFps) ?? "30"
            : DisplayLabel.Replace(" fps", "", StringComparison.OrdinalIgnoreCase);
    }

    private static double? ParseNominalFps(string label)
    {
        return double.TryParse(label, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static string? FormatNominalFps(double? fps)
    {
        if (!fps.HasValue)
        {
            return null;
        }

        return Math.Abs(fps.Value - Math.Round(fps.Value)) < 0.001
            ? Math.Round(fps.Value).ToString("0", System.Globalization.CultureInfo.InvariantCulture)
            : fps.Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
    }
}
