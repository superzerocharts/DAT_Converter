namespace DatConverter;

public sealed class MirasysFpsTechnicalDetails
{
    public int FrameCount { get; init; }
    public int H264KeyframeCount { get; init; }
    public int I264InterframeCount { get; init; }
    public ulong? FirstTimestamp { get; init; }
    public ulong? LastTimestamp { get; init; }
    public ulong? StreamTimestampSpan { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
    public bool MultipleResolutionsDetected { get; init; }
    public double? TimebaseUnitsPerSecond { get; init; }
    public double? DurationSeconds { get; init; }
    public double? AverageFps { get; init; }
    public double? InstantFpsMedian { get; init; }
    public double? InstantFpsMin { get; init; }
    public double? InstantFpsMax { get; init; }
    public double? BucketMedianFps { get; init; }
    public int? BucketModeFps { get; init; }
    public int? BucketMinFps { get; init; }
    public int? BucketMaxFps { get; init; }
    public int BucketCount { get; init; }
    public IReadOnlyList<ulong> PositiveTimestampDeltas { get; init; } = Array.Empty<ulong>();
    public IReadOnlyList<int> PerSecondBucketCounts { get; init; } = Array.Empty<int>();
    public IReadOnlyList<int> StableBucketCounts { get; init; } = Array.Empty<int>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}
