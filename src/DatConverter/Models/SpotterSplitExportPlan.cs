using System.Globalization;
using System.Text;

namespace DatConverter;

public sealed class SpotterSplitExportPlan
{
    public string ExportFolder { get; init; } = "";

    public string? SelectedSourcePath { get; init; }

    public string? LogicalOutputBaseName { get; init; }

    public IReadOnlyList<SpotterSplitExportSegment> Segments { get; init; } = Array.Empty<SpotterSplitExportSegment>();

    public int SegmentCount => Segments.Count;

    public int? SelectedSegmentNumber { get; init; }

    public string Confidence { get; init; } = "None";

    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Evidence { get; init; } = Array.Empty<string>();

    public bool IsSplitExport => SegmentCount > 1;

    public bool IsStrongConfidence => string.Equals(Confidence, "Strong", StringComparison.Ordinal);

    public string BuildTechnicalReport()
    {
        var builder = new StringBuilder();
        builder.AppendLine("Split export plan");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Export folder: {ExportFolder}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Selected source: {SelectedSourcePath ?? "none"}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Segment count: {SegmentCount}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Selected segment: {SelectedSegmentNumber?.ToString(CultureInfo.InvariantCulture) ?? "none"}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Confidence: {Confidence}");

        if (Evidence.Count > 0)
        {
            builder.AppendLine("Evidence:");
            foreach (var item in Evidence)
            {
                builder.AppendLine(CultureInfo.InvariantCulture, $"- {item}");
            }
        }

        if (Warnings.Count > 0)
        {
            builder.AppendLine("Warnings:");
            foreach (var warning in Warnings)
            {
                builder.AppendLine(CultureInfo.InvariantCulture, $"- {warning}");
            }
        }

        if (Segments.Count > 0)
        {
            builder.AppendLine("Segments:");
            foreach (var segment in Segments)
            {
                builder.AppendLine(
                    CultureInfo.InvariantCulture,
                    $"- {segment.SegmentNumber}: {segment.FileName}; start={FormatTime(segment.StartTime)}; end={FormatTime(segment.EndTime)}; duration={FormatDuration(segment.Duration)}; gap_from_previous={FormatDuration(segment.GapFromPrevious)}; path={segment.FilePath}");
            }
        }

        return builder.ToString();
    }

    private static string FormatTime(DateTime? value)
    {
        return value.HasValue
            ? value.Value.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)
            : "unknown";
    }

    private static string FormatDuration(TimeSpan? value)
    {
        return value.HasValue
            ? value.Value.TotalSeconds.ToString("0.###s", CultureInfo.InvariantCulture)
            : "unknown";
    }
}
