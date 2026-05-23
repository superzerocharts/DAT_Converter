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
    public const string FastFailedMessage = "Fast mode failed. Try Full mode.";
    public const string FullFailedMessage = "Full mode failed. This .dat file may be unsupported or corrupt.";
    public const string CanceledMessage = "Conversion canceled.";

    public string CommandLine => $"{QuoteForLog(FfmpegPath)} {string.Join(" ", Arguments.Select(QuoteForLog))}";

    private static string QuoteForLog(string value)
    {
        return value.Contains(' ') ? $"\"{value}\"" : value;
    }
}
