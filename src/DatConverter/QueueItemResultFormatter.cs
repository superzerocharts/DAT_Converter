namespace DatConverter;

public static class QueueItemResultFormatter
{
    public static string BuildLogLine(QueueItem item, int ordinal, int totalItems)
    {
        var status = GetStatusSummary(item);
        var output = GetOutputSummary(item);
        var exitCode = item.ConversionResult is { IsSuccess: false, ExitCode: not null } result
            ? $"; Exit code: {result.ExitCode}"
            : string.Empty;

        return $"Queue item result: {ordinal} of {totalItems}; {Path.GetFileName(item.InputPath)}; {status}; Output: {output}{exitCode}";
    }

    public static string BuildSummaryLine(QueueItem item, int ordinal, int totalItems)
    {
        return $"{ordinal} of {totalItems} - {GetStatusSummary(item)} - {Path.GetFileName(item.InputPath)}";
    }

    public static string GetStatusSummary(QueueItem item)
    {
        if (item.ConversionResult is not null)
        {
            return StripStatusPrefix(item.ConversionResult.StatusSummary);
        }

        if (!string.IsNullOrWhiteSpace(item.ResultStatusSummary))
        {
            return item.ResultStatusSummary;
        }

        return item.Status switch
        {
            QueueItemStatus.Completed => "Completed",
            QueueItemStatus.Canceled => "Canceled",
            QueueItemStatus.Unsupported => "Skipped - unsupported video payload",
            QueueItemStatus.Skipped when IsExistingOutput(item) => "Skipped - output already exists",
            QueueItemStatus.Skipped => "Skipped",
            QueueItemStatus.Invalid or QueueItemStatus.Failed => "Skipped - invalid output path",
            _ => item.StatusText
        };
    }

    public static string GetOutputSummary(QueueItem item)
    {
        if (item.ConversionResult?.IsSuccess == true)
        {
            return item.ConversionResult.OutputPath;
        }

        if (item.Status == QueueItemStatus.Unsupported)
        {
            return "not created";
        }

        return string.IsNullOrWhiteSpace(item.PlannedOutputPath)
            ? "not created"
            : item.PlannedOutputPath;
    }

    private static bool IsExistingOutput(QueueItem item)
    {
        return item.HasExistingDirectOutput ||
               string.Equals(item.StatusText, "Exists", StringComparison.OrdinalIgnoreCase) ||
               item.ProgressText.Contains("exists", StringComparison.OrdinalIgnoreCase);
    }

    private static string StripStatusPrefix(string statusSummary)
    {
        const string prefix = "Status: ";
        return statusSummary.StartsWith(prefix, StringComparison.Ordinal)
            ? statusSummary[prefix.Length..]
            : statusSummary;
    }
}
