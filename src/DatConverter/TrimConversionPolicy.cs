namespace DatConverter;

public static class TrimConversionPolicy
{
    public const string FastModeOnlyMessage = "Trim Video does not support the selected conversion mode.";
    public const string FastModeNote = "Fast may start near the selected time. Full is slower but more exact.";
    public const string FastConversionMode = "Fast";

    public static bool IsTrimSupportedForConversionMode(string conversionMode)
    {
        return string.Equals(conversionMode, "Fast", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(conversionMode, "Remux", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(conversionMode, "Encode", StringComparison.OrdinalIgnoreCase);
    }

    public static string ResolveModeForTrim(TrimRange? trimRange, string requestedConversionMode)
    {
        return trimRange is null || IsTrimSupportedForConversionMode(requestedConversionMode)
            ? requestedConversionMode
            : FastConversionMode;
    }

    public static bool CanSelectMode(TrimRange? trimRange)
    {
        return true;
    }

    public static bool ShouldBlockTrimmedConversion(TrimRange? trimRange, string conversionMode)
    {
        return trimRange is not null && !IsTrimSupportedForConversionMode(conversionMode);
    }
}
