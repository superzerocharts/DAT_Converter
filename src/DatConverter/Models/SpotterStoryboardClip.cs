namespace DatConverter;

public sealed class SpotterStoryboardClip
{
    public int ClipNumber { get; init; }

    public string ClipName { get; init; } = "";

    public DateTime? ClipStartTime { get; init; }

    public DateTime? ClipEndTime { get; init; }

    public string NodePath { get; init; } = "";

    public int? ChannelId { get; init; }

    public string? CameraDisplayName { get; init; }

    public string DatFileName { get; init; } = "";

    public string DatFilePath { get; init; } = "";

    public DateTime? MaterialStartTime { get; init; }

    public DateTime? MaterialEndTime { get; init; }

    public string? SourceFpsLabel { get; set; }

    public string? SourceFfmpegFpsValue { get; set; }

    public string? SourceFpsDecisionReason { get; set; }

    public DateTime? EffectiveStartTime => MaterialStartTime ?? ClipStartTime;

    public DateTime? EffectiveEndTime => MaterialEndTime ?? ClipEndTime;

    public TimeSpan? EffectiveDuration => EffectiveStartTime.HasValue && EffectiveEndTime.HasValue && EffectiveEndTime.Value > EffectiveStartTime.Value
        ? EffectiveEndTime.Value - EffectiveStartTime.Value
        : null;
}
