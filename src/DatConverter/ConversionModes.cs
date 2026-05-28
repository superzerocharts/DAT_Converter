namespace DatConverter;

public static class ConversionModes
{
    public const string Remux = "Remux";
    public const string Fast = "Fast";
    public const string Encode = "Encode";
    public const string EncodeNvenc = "EncodeNvenc";

    public const string FastDisplayName = "Fast";
    public const string FullDisplayName = "Full";
    public const string FullNvencDisplayName = "Full NVENC";

    public static readonly string[] DisplayOrder =
    [
        FastDisplayName,
        FullDisplayName,
        FullNvencDisplayName
    ];

    public static string ParseDisplay(string? value)
    {
        if (string.Equals(value, FullNvencDisplayName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, EncodeNvenc, StringComparison.OrdinalIgnoreCase))
        {
            return EncodeNvenc;
        }

        return string.Equals(value, FullDisplayName, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, Encode, StringComparison.OrdinalIgnoreCase)
            ? Encode
            : Remux;
    }

    public static string FormatDisplay(string? conversionMode)
    {
        if (string.Equals(conversionMode, EncodeNvenc, StringComparison.OrdinalIgnoreCase))
        {
            return FullNvencDisplayName;
        }

        return string.Equals(conversionMode, Encode, StringComparison.OrdinalIgnoreCase)
            ? FullDisplayName
            : FastDisplayName;
    }

    public static bool IsEncode(string? conversionMode)
    {
        return string.Equals(conversionMode, Encode, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(conversionMode, EncodeNvenc, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsNvenc(string? conversionMode)
    {
        return string.Equals(conversionMode, EncodeNvenc, StringComparison.OrdinalIgnoreCase);
    }
}
