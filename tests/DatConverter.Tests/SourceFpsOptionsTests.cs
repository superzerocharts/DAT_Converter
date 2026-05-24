namespace DatConverter.Tests;

public sealed class SourceFpsOptionsTests
{
    [Fact]
    public void DisplayOrder_KeepsAutoDetectFirstThenManualValuesDescending()
    {
        Assert.Equal(
            new[] { "Auto-detect", "30", "29.97", "25", "24", "20", "15" },
            SourceFpsOptions.DisplayOrder);
    }

    [Theory]
    [InlineData("30", "30")]
    [InlineData("29.97", "30000/1001")]
    [InlineData("25", "25")]
    [InlineData("24", "24")]
    [InlineData("20", "20")]
    [InlineData("15", "15")]
    public void ManualOptions_KeepExpectedFfmpegMappings(string label, string expectedFfmpegValue)
    {
        var fps = FpsOption.FromLabel(label);

        Assert.Equal(expectedFfmpegValue, fps.FfmpegValue);
    }
}
