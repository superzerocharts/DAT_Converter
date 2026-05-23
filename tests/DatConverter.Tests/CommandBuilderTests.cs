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
        AssertOptionValue(arguments, "-map", "0:v:0");
        AssertOptionValue(arguments, "-i", @"C:\demo input\clip.dat");
        Assert.Contains("-an", arguments);
        Assert.Contains("-sn", arguments);
        Assert.Contains("-dn", arguments);
        AssertOptionValue(arguments, "-tag:v", "avc1");
        AssertOptionValue(arguments, "-avoid_negative_ts", "make_zero");
        AssertOptionValue(arguments, "-video_track_timescale", "90000");
        AssertOptionValue(arguments, "-movflags", "+faststart");
        Assert.DoesNotContain("libx264", arguments);
        Assert.DoesNotContain("-crf", arguments);
        Assert.DoesNotContain("-preset", arguments);
        Assert.DoesNotContain("-vf", arguments);
        Assert.DoesNotContain("format=yuv420p", arguments);
    }

    [Fact]
    public void RemuxArguments_Mp4UseExactCompatibilityArgumentList()
    {
        var arguments = FfmpegCommandBuilder.BuildRemuxArguments(
            @"C:\demo input\clip.dat",
            @"C:\demo output\clip.mp4",
            OutputFormat.Mp4,
            FpsOption.FromLabel("30"));

        Assert.Equal(
            new[]
            {
                "-n",
                "-nostats",
                "-progress",
                "pipe:1",
                "-fflags",
                "+genpts+discardcorrupt",
                "-err_detect",
                "ignore_err",
                "-f",
                "h264",
                "-r",
                "30",
                "-i",
                @"C:\demo input\clip.dat",
                "-map",
                "0:v:0",
                "-an",
                "-sn",
                "-dn",
                "-c:v",
                "copy",
                "-tag:v",
                "avc1",
                "-avoid_negative_ts",
                "make_zero",
                "-video_track_timescale",
                "90000",
                "-movflags",
                "+faststart",
                @"C:\demo output\clip.mp4"
            },
            arguments);
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
        Assert.DoesNotContain("-map", arguments);
        Assert.DoesNotContain("-an", arguments);
        Assert.DoesNotContain("-sn", arguments);
        Assert.DoesNotContain("-dn", arguments);
        Assert.DoesNotContain("-bsf:v", arguments);
        Assert.DoesNotContain("-tag:v", arguments);
        Assert.DoesNotContain("-avoid_negative_ts", arguments);
        Assert.DoesNotContain("-video_track_timescale", arguments);
        Assert.DoesNotContain("-brand", arguments);
    }

    [Fact]
    public void RemuxArguments_Keep2997SourceFpsMapping()
    {
        var arguments = FfmpegCommandBuilder.BuildRemuxArguments(
            @"C:\in\clip.dat",
            @"C:\out\clip.mp4",
            OutputFormat.Mp4,
            FpsOption.FromLabel("29.97"));

        AssertOptionValue(arguments, "-r", "30000/1001");
        AssertOptionValue(arguments, "-video_track_timescale", "90000");
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
