namespace DatConverter;

public sealed class TrimPreviewFrameResult
{
    public bool Succeeded { get; init; }

    public string? ImagePath { get; init; }

    public string TechnicalDetails { get; init; } = "";
}
