namespace DatConverter;

public sealed class TrimPreviewFrameRequest
{
    public required string FfmpegPath { get; init; }

    public required string SourcePath { get; init; }

    public required string PreviewInputPath { get; init; }

    public required string OutputImagePath { get; init; }

    public TimeSpan TimelineOffset { get; init; }

    public TimeSpan SourceLocalOffset { get; init; }

    public TimeSpan PreviewSeekOffset { get; init; }

    public TimeSpan? SelectedKeyframeLocalOffset { get; init; }

    public required IReadOnlyList<string> Arguments { get; init; }
}
