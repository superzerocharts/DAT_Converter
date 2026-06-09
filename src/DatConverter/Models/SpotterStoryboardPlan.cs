using System.Globalization;
using System.Text;

namespace DatConverter;

public sealed class SpotterStoryboardPlan
{
    public string ExportFolder { get; init; } = "";

    public string SidecarPath { get; init; } = "";

    public string StoryboardName { get; init; } = "Storyboard";

    public IReadOnlyList<SpotterStoryboardClip> Clips { get; init; } = Array.Empty<SpotterStoryboardClip>();

    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Evidence { get; init; } = Array.Empty<string>();

    public int ClipCount => Clips.Count;

    public bool IsStoryboardExport => ClipCount > 0;

    public bool IsStrongConfidence => IsStoryboardExport && Warnings.Count == 0;

    public string BuildTechnicalReport()
    {
        var builder = new StringBuilder();
        builder.AppendLine("Storyboard export plan");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Export folder: {ExportFolder}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Metadata file: {(string.IsNullOrWhiteSpace(SidecarPath) ? "none" : SidecarPath)}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Storyboard name: {StoryboardName}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Clip count: {ClipCount}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Confidence: {(IsStrongConfidence ? "Strong" : "Weak")}");

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

        if (Clips.Count > 0)
        {
            builder.AppendLine("Clips:");
            foreach (var clip in Clips)
            {
                builder.AppendLine(
                    CultureInfo.InvariantCulture,
                    $"- {clip.ClipNumber}: {clip.ClipName}; channel={clip.ChannelId?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}; camera={clip.CameraDisplayName ?? "unknown"}; dat={clip.DatFileName}; start={FormatTime(clip.EffectiveStartTime)}; end={FormatTime(clip.EffectiveEndTime)}; node={clip.NodePath}");
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
}
