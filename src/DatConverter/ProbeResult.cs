namespace DatConverter;

public sealed record ProbeResult(
    bool IsSuccess,
    string UserMessage,
    string ToolPath,
    FpsOption Fps,
    string? CodecName = null,
    string? Profile = null,
    int? Width = null,
    int? Height = null,
    string? PixelFormat = null,
    string? RFrameRate = null,
    string? AvgFrameRate = null,
    string? Duration = null,
    string TechnicalDetails = "")
{
    public const string UnsupportedMessage = "This .dat file could not be interpreted as raw H.264. It may not be a compatible video payload.";
}
