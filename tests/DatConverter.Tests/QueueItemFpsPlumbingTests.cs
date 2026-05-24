namespace DatConverter.Tests;

public sealed class QueueItemFpsPlumbingTests
{
    [Fact]
    public void PendingAutoDetect_IsUnresolvedAndKeepsAutoSelectionMode()
    {
        var resolution = QueueItemFpsResolution.PendingAutoDetect();

        Assert.Equal(FpsSelectionMode.AutoDetect, resolution.SelectionMode);
        Assert.False(resolution.HasResolvedFps);
        Assert.True(resolution.RequiresManualFpsSelection);
        Assert.Equal("", resolution.FfmpegRateValue);
        Assert.Equal("Detecting source frame rate...", resolution.FpsValidationMessage);
    }

    [Fact]
    public void QueueItem_ManualFpsStoresEffectiveFps()
    {
        var item = CreateItem(FpsOption.FromLabel("29.97"));

        Assert.Equal(FpsSelectionMode.Manual, item.FpsSelectionMode);
        Assert.Equal("29.97", item.FpsDisplayLabel);
        Assert.Equal("30000/1001", item.FfmpegRateValue);
        Assert.Equal("30000/1001", item.Fps.FfmpegValue);
        Assert.Equal("Manual", item.FpsConfidence);
    }

    [Fact]
    public void BatchItemsCanCarryMixedAutoFpsDecisions()
    {
        var first = CreateItem(FpsOption.FromLabel("30"));
        var second = CreateItem(FpsOption.FromLabel("30"));
        first.ApplyFpsResolution(new QueueItemFpsResolution
        {
            SelectionMode = FpsSelectionMode.AutoDetect,
            DisplayLabel = "Auto 30",
            FfmpegRateValue = "30",
            NominalConversionFps = 30,
            AutoDetectionSucceeded = true,
            Confidence = "High"
        });
        second.ApplyFpsResolution(new QueueItemFpsResolution
        {
            SelectionMode = FpsSelectionMode.AutoDetect,
            DisplayLabel = "Auto 25",
            FfmpegRateValue = "25",
            NominalConversionFps = 25,
            AutoDetectionSucceeded = true,
            Confidence = "High"
        });

        Assert.Equal("30", first.FfmpegRateValue);
        Assert.Equal("25", second.FfmpegRateValue);
        Assert.Equal("30", first.Fps.FfmpegValue);
        Assert.Equal("25", second.Fps.FfmpegValue);
    }

    [Fact]
    public void CommandBuilderUsesQueueItemEffectiveFfmpegRate()
    {
        var item = CreateItem(FpsOption.FromLabel("30"));
        item.ApplyFpsResolution(new QueueItemFpsResolution
        {
            SelectionMode = FpsSelectionMode.AutoDetect,
            DisplayLabel = "Auto 25",
            FfmpegRateValue = "25",
            NominalConversionFps = 25,
            AutoDetectionSucceeded = true,
            Confidence = "High"
        });

        var remux = FfmpegCommandBuilder.BuildRemuxArguments(item.InputPath, item.PlannedOutputPath, item.OutputFormat, item.Fps);
        var encode = FfmpegCommandBuilder.BuildEncodeArguments(item.InputPath, item.PlannedOutputPath, item.OutputFormat, item.Fps);

        Assert.Equal("25", GetOptionValue(remux, "-r"));
        Assert.Equal("25", GetOptionValue(encode, "-r"));
        Assert.Contains("fps=25", GetOptionValue(encode, "-vf"));
    }

    [Fact]
    public void AutoDetectionFailureStoresUnresolvedFpsOnQueueItem()
    {
        var item = CreateItem(FpsOption.FromLabel("30"));
        item.ApplyFpsResolution(new QueueItemFpsResolution
        {
            SelectionMode = FpsSelectionMode.AutoDetect,
            DisplayLabel = "Needs manual selection",
            FfmpegRateValue = "",
            NominalConversionFps = null,
            HasResolvedFps = false,
            RequiresManualFpsSelection = true,
            FpsValidationMessage = "Auto-detect could not determine the source FPS. Double-click this row and choose Source FPS.",
            Confidence = "Unavailable"
        });

        Assert.False(item.HasResolvedFps);
        Assert.True(item.RequiresManualFpsSelection);
        Assert.Equal("Needs manual selection", item.FpsDisplayLabel);
        Assert.Equal("", item.FfmpegRateValue);
        Assert.Equal("Needs FPS", item.Fps.Label);
        Assert.Equal("", item.Fps.FfmpegValue);
    }

    [Fact]
    public void CommandBuilderRejectsUnresolvedFps()
    {
        var fps = new FpsOption("Needs FPS", "");

        var remuxError = Assert.Throws<InvalidOperationException>(() =>
            FfmpegCommandBuilder.BuildRemuxArguments(@"C:\input\clip.dat", @"C:\output\clip.mp4", OutputFormat.Mp4, fps));
        var encodeError = Assert.Throws<InvalidOperationException>(() =>
            FfmpegCommandBuilder.BuildEncodeArguments(@"C:\input\clip.dat", @"C:\output\clip.mp4", OutputFormat.Mp4, fps));

        Assert.Contains("Source FPS is not set", remuxError.Message);
        Assert.Contains("Source FPS is not set", encodeError.Message);
    }

    [Theory]
    [InlineData("25", "25")]
    [InlineData("29.97", "30000/1001")]
    public void CommandBuilderUsesQueueItemEffectiveFpsForAllFormatsAndModes(string displayLabel, string ffmpegRateValue)
    {
        var item = CreateItem(FpsOption.FromLabel("30"));
        item.ApplyFpsResolution(new QueueItemFpsResolution
        {
            SelectionMode = FpsSelectionMode.Manual,
            DisplayLabel = displayLabel,
            FfmpegRateValue = ffmpegRateValue,
            NominalConversionFps = displayLabel == "29.97" ? 29.97 : 25,
            Confidence = "Manual"
        });

        var remuxMp4 = FfmpegCommandBuilder.BuildRemuxArguments(item.InputPath, item.PlannedOutputPath, OutputFormat.Mp4, item.Fps);
        var remuxMkv = FfmpegCommandBuilder.BuildRemuxArguments(item.InputPath, @"C:\output\clip.mkv", OutputFormat.Mkv, item.Fps);
        var encodeMp4 = FfmpegCommandBuilder.BuildEncodeArguments(item.InputPath, item.PlannedOutputPath, OutputFormat.Mp4, item.Fps);
        var encodeMkv = FfmpegCommandBuilder.BuildEncodeArguments(item.InputPath, @"C:\output\clip.mkv", OutputFormat.Mkv, item.Fps);

        Assert.Equal(ffmpegRateValue, GetOptionValue(remuxMp4, "-r"));
        Assert.Equal(ffmpegRateValue, GetOptionValue(remuxMkv, "-r"));
        Assert.Equal(ffmpegRateValue, GetOptionValue(encodeMp4, "-r"));
        Assert.Equal(ffmpegRateValue, GetOptionValue(encodeMkv, "-r"));
        Assert.Contains($"fps={ffmpegRateValue}", GetOptionValue(encodeMp4, "-vf"));
        Assert.Contains($"fps={ffmpegRateValue}", GetOptionValue(encodeMkv, "-vf"));
    }

    private static QueueItem CreateItem(FpsOption fps)
    {
        return new QueueItem(
            @"C:\input\clip.dat",
            @"C:\output\clip.mp4",
            OutputDestinationMode.SameFolderAsSource,
            null,
            OutputFormat.Mp4,
            "Remux",
            fps,
            hasExistingDirectOutput: false);
    }

    private static string GetOptionValue(IReadOnlyList<string> arguments, string option)
    {
        var index = arguments.ToList().IndexOf(option);
        Assert.True(index >= 0 && index < arguments.Count - 1, $"Missing option value for {option}.");
        return arguments[index + 1];
    }
}
