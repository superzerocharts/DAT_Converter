namespace DatConverter.Tests;

public sealed class InternalConversionPathOptionsTests
{
    [Fact]
    public void FromEnvironment_DefaultsToCleanRemuxEnabled()
    {
        var previous = Environment.GetEnvironmentVariable("DAT_CONVERTER_DISABLE_CLEAN_REMUX");
        try
        {
            Environment.SetEnvironmentVariable("DAT_CONVERTER_DISABLE_CLEAN_REMUX", null);

            var options = InternalConversionPathOptions.FromEnvironment();

            Assert.False(options.DisableCleanRemux);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DAT_CONVERTER_DISABLE_CLEAN_REMUX", previous);
        }
    }

    [Fact]
    public void FromEnvironment_DisableValueForcesStandardPath()
    {
        var previous = Environment.GetEnvironmentVariable("DAT_CONVERTER_DISABLE_CLEAN_REMUX");
        try
        {
            Environment.SetEnvironmentVariable("DAT_CONVERTER_DISABLE_CLEAN_REMUX", "1");

            var options = InternalConversionPathOptions.FromEnvironment();

            Assert.True(options.DisableCleanRemux);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DAT_CONVERTER_DISABLE_CLEAN_REMUX", previous);
        }
    }
}
