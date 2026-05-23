namespace DatConverter;

public enum OutputFormat
{
    Mp4,
    Mkv
}

public static class OutputFormatExtensions
{
    public static OutputFormat Parse(string? value)
    {
        return string.Equals(value, "MKV", StringComparison.OrdinalIgnoreCase)
            ? OutputFormat.Mkv
            : OutputFormat.Mp4;
    }

    public static string DisplayName(this OutputFormat outputFormat)
    {
        return outputFormat == OutputFormat.Mkv ? "MKV" : "MP4";
    }

    public static string Extension(this OutputFormat outputFormat)
    {
        return outputFormat == OutputFormat.Mkv ? ".mkv" : ".mp4";
    }

    public static bool IsMp4(this OutputFormat outputFormat)
    {
        return outputFormat == OutputFormat.Mp4;
    }
}
