namespace DatConverter;

public sealed class DatPreviewWindowResult
{
    public bool Succeeded { get; init; }

    public string? OutputPath { get; init; }

    public string? FailureReason { get; init; }

    public string SourcePath { get; init; } = "";

    public TimeSpan RequestedLocalOffset { get; init; }

    public TimeSpan? SelectedKeyframeLocalOffset { get; init; }

    public TimeSpan PreviewSeekOffset { get; init; }

    public int ScannedFrameRecordCount { get; init; }

    public int WrittenFrameRecordCount { get; init; }

    public long WrittenPayloadBytes { get; init; }

    public string TechnicalDetails { get; init; } = "";
}
