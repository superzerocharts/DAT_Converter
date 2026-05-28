namespace DatConverter;

public sealed record InternalConversionPathOptions(bool DisableCleanRemux)
{
    public static InternalConversionPathOptions Default { get; } = new(false);

    public static InternalConversionPathOptions FromEnvironment()
    {
        var value = Environment.GetEnvironmentVariable("DAT_CONVERTER_DISABLE_CLEAN_REMUX");
        return new InternalConversionPathOptions(string.Equals(value, "1", StringComparison.Ordinal));
    }
}
