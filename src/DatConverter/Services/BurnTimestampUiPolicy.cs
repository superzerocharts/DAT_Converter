namespace DatConverter;

public static class BurnTimestampUiPolicy
{
    public static BurnTimestampUiState Evaluate(string? conversionMode, bool hasRecordingTime, bool requestedChecked)
    {
        var isFullMode = string.Equals(conversionMode, "Encode", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(conversionMode, "Full", StringComparison.OrdinalIgnoreCase);
        var enabled = isFullMode && hasRecordingTime;
        return new BurnTimestampUiState(
            enabled,
            enabled && requestedChecked,
            hasRecordingTime
                ? BurnTimestampMetadataBuilder.ReliabilityNote
                : BurnTimestampMetadataBuilder.RequiresRecordingDateTimeMessage);
    }
}

public sealed record BurnTimestampUiState(
    bool Enabled,
    bool Checked,
    string Note);
