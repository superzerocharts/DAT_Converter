namespace DatConverter.Tests;

public sealed class CommandBuilderTests
{
    [Theory]
    [InlineData("15", "15")]
    [InlineData("20", "20")]
    [InlineData("24", "24")]
    [InlineData("25", "25")]
    [InlineData("29.97", "30000/1001")]
    [InlineData("30", "30")]
    public void FpsOption_MapsSupportedLabelsToFfmpegValues(string label, string expectedValue)
    {
        var fps = FpsOption.FromLabel(label);

        Assert.Equal(label, fps.Label);
        Assert.Equal(expectedValue, fps.FfmpegValue);
    }

    [Fact]
    public void RemuxArguments_CopyVideoAndDoNotIncludeEncodeOnlyOptions()
    {
        var arguments = FfmpegCommandBuilder.BuildRemuxArguments(
            @"C:\demo input\clip.dat",
            @"C:\demo output\clip.mp4",
            OutputFormat.Mp4,
            FpsOption.FromLabel("30"));

        Assert.Contains("-n", arguments);
        Assert.DoesNotContain("-y", arguments);
        AssertOptionValue(arguments, "-c:v", "copy");
        AssertOptionValue(arguments, "-r", "30");
        AssertOptionValue(arguments, "-i", @"C:\demo input\clip.dat");
        Assert.Contains("+faststart", arguments);
        Assert.DoesNotContain("libx264", arguments);
        Assert.DoesNotContain("-crf", arguments);
        Assert.DoesNotContain("-preset", arguments);
        Assert.DoesNotContain("-vf", arguments);
        Assert.DoesNotContain("-an", arguments);
    }

    [Fact]
    public void RemuxArguments_MkvDoesNotIncludeFaststart()
    {
        var arguments = FfmpegCommandBuilder.BuildRemuxArguments(
            @"C:\in\clip.dat",
            @"C:\out\clip.mkv",
            OutputFormat.Mkv,
            FpsOption.FromLabel("25"));

        Assert.DoesNotContain("-movflags", arguments);
        Assert.DoesNotContain("+faststart", arguments);
    }

    [Fact]
    public void EncodeArguments_UseRequiredX264SettingsAnd2997Consistently()
    {
        var arguments = FfmpegCommandBuilder.BuildEncodeArguments(
            @"C:\in\clip.dat",
            @"C:\out\clip.mp4",
            OutputFormat.Mp4,
            FpsOption.FromLabel("29.97"));

        Assert.Contains("-n", arguments);
        Assert.DoesNotContain("-y", arguments);
        AssertOptionValue(arguments, "-r", "30000/1001");
        AssertOptionValue(arguments, "-c:v", "libx264");
        AssertOptionValue(arguments, "-preset", "veryfast");
        AssertOptionValue(arguments, "-crf", "22");
        Assert.Contains("-an", arguments);
        Assert.Contains("+faststart", arguments);

        var filter = GetOptionValue(arguments, "-vf");
        Assert.Contains("setpts=N/((30000/1001)*TB)", filter);
        Assert.Contains("fps=30000/1001", filter);
        Assert.Contains("format=yuv420p", filter);
    }

    [Fact]
    public void EncodeArguments_MkvDoesNotIncludeFaststart()
    {
        var arguments = FfmpegCommandBuilder.BuildEncodeArguments(
            @"C:\in\clip.dat",
            @"C:\out\clip.mkv",
            OutputFormat.Mkv,
            FpsOption.FromLabel("24"));

        Assert.DoesNotContain("-movflags", arguments);
        Assert.DoesNotContain("+faststart", arguments);
    }

    private static void AssertOptionValue(IReadOnlyList<string> arguments, string option, string expectedValue)
    {
        Assert.Equal(expectedValue, GetOptionValue(arguments, option));
    }

    private static string GetOptionValue(IReadOnlyList<string> arguments, string option)
    {
        var index = arguments.ToList().IndexOf(option);
        Assert.True(index >= 0 && index < arguments.Count - 1, $"Missing option value for {option}.");
        return arguments[index + 1];
    }
}
