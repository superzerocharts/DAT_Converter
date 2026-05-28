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
    bool UsedDeterminateProgress = false,
    TimeSpan? ProcessingTime = null,
    ConversionInputPathMode InputPathMode = ConversionInputPathMode.StandardWholeDatRawH264)
{
    public const string FastFailedMessage = "Fast mode failed. Try Full mode.";
    public const string FullFailedMessage = "Full mode failed. This .dat file may be unsupported or corrupt.";
    public const string CanceledMessage = "Conversion canceled.";

    public string CommandLine => $"{QuoteForLog(FfmpegPath)} {string.Join(" ", Arguments.Select(QuoteForLog))}";

    public string StatusSummary
    {
        get
        {
            var status = TimedOut
                ? "Timed out"
                : WasCanceled
                    ? "Canceled"
                    : IsSuccess
                        ? "Completed"
                        : "Failed";
            var connector = IsSuccess ? "in" : "after";
            return ProcessingTime.HasValue
                ? $"Status: {status} {connector} {FormatProcessingTime(ProcessingTime.Value)}"
                : $"Status: {status}";
        }
    }

    private static string QuoteForLog(string value)
    {
        return value.Contains(' ') ? $"\"{value}\"" : value;
    }

    private static string FormatProcessingTime(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        return elapsed.TotalSeconds < 60
            ? $"{elapsed.TotalSeconds:0.0} seconds"
            : elapsed.TotalHours >= 1
                ? elapsed.ToString(@"h\:mm\:ss", System.Globalization.CultureInfo.InvariantCulture)
                : elapsed.ToString(@"mm\:ss", System.Globalization.CultureInfo.InvariantCulture);
    }
}
