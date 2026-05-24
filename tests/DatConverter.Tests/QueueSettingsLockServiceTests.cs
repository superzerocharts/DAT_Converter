namespace DatConverter.Tests;

public sealed class QueueSettingsLockServiceTests
{
    [Fact]
    public void ApplyLockedSettings_OverwritesPreviouslyQueuedItemSettings()
    {
        var item = new QueueItem(
            @"C:\input\clip.dat",
            @"C:\old-output\clip.mp4",
            OutputDestinationMode.SameFolderAsSource,
            null,
            OutputFormat.Mp4,
            "Remux",
            FpsOption.FromLabel("30"),
            true)
        {
            Status = QueueItemStatus.Ready,
            StatusText = "Ready",
            ProgressText = "Ready"
        };

        var lockedSettings = new QueueSettingsSnapshot(
            OutputFormat.Mkv,
            "Encode",
            FpsOption.FromLabel("29.97"),
            OutputDestinationMode.ChooseOutputFolder,
            @"C:\locked-output");

        QueueSettingsLockService.ApplyLockedSettings(
            item,
            lockedSettings,
            @"C:\locked-output",
            @"C:\locked-output\clip.mkv",
            hasExistingDirectOutput: false,
            readyProgressText: "1920x1080");

        Assert.Equal(OutputFormat.Mkv, item.OutputFormat);
        Assert.Equal("Encode", item.ConversionMode);
        Assert.Equal("29.97", item.Fps.Label);
        Assert.Equal("30000/1001", item.Fps.FfmpegValue);
        Assert.Equal(OutputDestinationMode.ChooseOutputFolder, item.OutputDestinationMode);
        Assert.Equal(@"C:\locked-output", item.SelectedOutputFolder);
        Assert.Equal(@"C:\locked-output\clip.mkv", item.PlannedOutputPath);
        Assert.False(item.HasExistingDirectOutput);
        Assert.Equal(QueueItemStatus.Ready, item.Status);
        Assert.Equal("Ready", item.StatusText);
        Assert.Equal("1920x1080", item.ProgressText);
    }

    [Fact]
    public void ApplyLockedSettings_UsesSameFolderWithoutSelectedOutputFolder()
    {
        var item = new QueueItem(
            @"C:\input\clip.dat",
            @"C:\old-output\clip.mkv",
            OutputDestinationMode.ChooseOutputFolder,
            @"C:\old-output",
            OutputFormat.Mkv,
            "Encode",
            FpsOption.FromLabel("24"),
            false);
        var lockedSettings = new QueueSettingsSnapshot(
            OutputFormat.Mp4,
            "Remux",
            FpsOption.FromLabel("30"),
            OutputDestinationMode.SameFolderAsSource,
            null);

        QueueSettingsLockService.ApplyLockedSettings(
            item,
            lockedSettings,
            @"C:\input",
            @"C:\input\clip.mp4",
            hasExistingDirectOutput: false,
            readyProgressText: "");

        Assert.Equal(OutputDestinationMode.SameFolderAsSource, item.OutputDestinationMode);
        Assert.Null(item.SelectedOutputFolder);
        Assert.Equal(OutputFormat.Mp4, item.OutputFormat);
        Assert.Equal("Remux", item.ConversionMode);
        Assert.Equal("30", item.Fps.Label);
        Assert.Equal(QueueItemStatus.Ready, item.Status);
    }

    [Fact]
    public void ApplyLockedSettings_MarksItemSkippedWhenLockedSettingsSayToSkipExistingDirectOutput()
    {
        var item = new QueueItem(
            @"C:\input\clip.dat",
            @"C:\old-output\clip.mp4",
            OutputDestinationMode.SameFolderAsSource,
            null,
            OutputFormat.Mp4,
            "Remux",
            FpsOption.FromLabel("30"),
            false);
        var lockedSettings = new QueueSettingsSnapshot(
            OutputFormat.Mp4,
            "Remux",
            FpsOption.FromLabel("30"),
            OutputDestinationMode.ChooseOutputFolder,
            @"C:\locked-output");

        QueueSettingsLockService.ApplyLockedSettings(
            item,
            lockedSettings,
            @"C:\locked-output",
            @"C:\locked-output\clip.mp4",
            hasExistingDirectOutput: true,
            readyProgressText: "Ready");

        Assert.True(item.HasExistingDirectOutput);
        Assert.Equal(QueueItemStatus.Skipped, item.Status);
        Assert.Equal("Exists", item.StatusText);
        Assert.Equal("Selected output exists", item.ProgressText);
    }

    [Fact]
    public void ApplyLockedSettings_AppliesResolvedItemFps()
    {
        var item = new QueueItem(
            @"C:\input\clip.dat",
            @"C:\old-output\clip.mp4",
            OutputDestinationMode.SameFolderAsSource,
            null,
            OutputFormat.Mp4,
            "Remux",
            FpsOption.FromLabel("30"),
            false);
        var lockedSettings = new QueueSettingsSnapshot(
            OutputFormat.Mp4,
            "Remux",
            FpsOption.FromLabel("30"),
            OutputDestinationMode.SameFolderAsSource,
            null)
        {
            FpsSettings = QueueItemFpsSettings.AutoDetect()
        };

        QueueSettingsLockService.ApplyLockedSettings(
            item,
            lockedSettings,
            @"C:\input",
            @"C:\input\clip.mp4",
            hasExistingDirectOutput: false,
            readyProgressText: "Ready",
            fpsResolution: new QueueItemFpsResolution
            {
                SelectionMode = FpsSelectionMode.AutoDetect,
                DisplayLabel = "Auto 25",
                FfmpegRateValue = "25",
                NominalConversionFps = 25,
                AutoDetectionSucceeded = true,
                Confidence = "High"
            });

        Assert.Equal(FpsSelectionMode.AutoDetect, item.FpsSelectionMode);
        Assert.Equal("Auto 25", item.FpsDisplayLabel);
        Assert.Equal("25", item.FfmpegRateValue);
        Assert.Equal("25", item.Fps.FfmpegValue);
    }
}
