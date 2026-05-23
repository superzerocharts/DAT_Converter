namespace DatConverter;

public sealed record ConversionResult(
    bool IsSuccess,
    string UserMessage,
    string FfmpegPath,
    IReadOnlyList<string> Arguments,
    string InputPath,
    string OutputPath,
    FpsOption Fps,
    int? ExitCode,
    string StandardOutput,
    string StandardError,
    string PartialOutputMessage = "",
    string ConversionMode = "",
    string OutputFormat = "",
    bool WasCanceled = false,
    bool TimedOut = false,
    TimeSpan? Duration = null,
    bool UsedDeterminateProgress = false)
{
    public const string RemuxFailedMessage = "Remux failed. This file may have timing or bitstream issues. Try Encode mode, which is slower but more tolerant.";
    public const string EncodeFailedMessage = "Encode failed. This .dat file may be unsupported or corrupt.";
    public const string CanceledMessage = "Conversion canceled.";

    public string CommandLine => $"{QuoteForLog(FfmpegPath)} {string.Join(" ", Arguments.Select(QuoteForLog))}";

    private static string QuoteForLog(string value)
    {
        return value.Contains(' ') ? $"\"{value}\"" : value;
    }
}
