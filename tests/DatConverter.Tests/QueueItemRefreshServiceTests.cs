namespace DatConverter.Tests;

public sealed class QueueItemRefreshServiceTests
{
    [Fact]
    public void FormatChangeFromMp4ToMkvRefreshesOutputAndClearsStaleExists()
    {
        using var temp = new TempDirectory();
        var inputPath = CreateFile(temp.Path, "sample_video.dat");
        CreateFile(temp.Path, "sample_video.mp4");
        var item = CreateReadyItem(inputPath, Path.Combine(temp.Path, "sample_video.mp4"), OutputFormat.Mp4, hasExistingDirectOutput: true);

        var result = Refresh([item], CurrentSettings(OutputFormat.Mkv));

        Assert.Equal(1, result.RefreshedCount);
        Assert.Equal(OutputFormat.Mkv, item.OutputFormat);
        Assert.Equal(Path.Combine(temp.Path, "sample_video.mkv"), item.PlannedOutputPath);
        Assert.False(item.HasExistingDirectOutput);
        Assert.Equal(QueueItemStatus.Ready, item.Status);
        Assert.Equal("Ready", item.StatusText);
    }

    [Fact]
    public void ExistingMkvMarksMkvQueueItemExistsUnconditionally()
    {
        using var temp = new TempDirectory();
        var inputPath = CreateFile(temp.Path, "clip.dat");
        CreateFile(temp.Path, "clip.mkv");
        var item = CreateReadyItem(inputPath, Path.Combine(temp.Path, "clip.mp4"), OutputFormat.Mp4);

        Refresh([item], CurrentSettings(OutputFormat.Mkv));

        Assert.Equal(OutputFormat.Mkv, item.OutputFormat);
        Assert.Equal(Path.Combine(temp.Path, "clip.mkv"), item.PlannedOutputPath);
        Assert.True(item.HasExistingDirectOutput);
        Assert.Equal(QueueItemStatus.Skipped, item.Status);
        Assert.Equal("Exists", item.StatusText);
        Assert.Equal("Selected output exists", item.ProgressText);
    }

    [Fact]
    public void ModeChangeRefreshesQueuedItemModeWithoutResettingValidProbe()
    {
        using var temp = new TempDirectory();
        var inputPath = CreateFile(temp.Path, "clip.dat");
        var item = CreateReadyItem(inputPath, Path.Combine(temp.Path, "clip.mp4"), OutputFormat.Mp4);

        Refresh([item], CurrentSettings(OutputFormat.Mp4, conversionMode: "Encode"));

        Assert.Equal("Encode", item.ConversionMode);
        Assert.Equal(QueueItemStatus.Ready, item.Status);
        Assert.NotNull(item.PreProbeResult);
    }

    [Fact]
    public void FpsChangeRefreshesQueuedItemFpsAndRequiresReprobe()
    {
        using var temp = new TempDirectory();
        var inputPath = CreateFile(temp.Path, "clip.dat");
        var item = CreateReadyItem(inputPath, Path.Combine(temp.Path, "clip.mp4"), OutputFormat.Mp4);

        Refresh([item], CurrentSettings(OutputFormat.Mp4, fps: FpsOption.FromLabel("29.97")));

        Assert.Equal("29.97", item.Fps.Label);
        Assert.Equal("30000/1001", item.Fps.FfmpegValue);
        Assert.Null(item.PreProbeResult);
        Assert.Equal(QueueItemStatus.WaitingForProbe, item.Status);
        Assert.Equal("Waiting for probe", item.StatusText);
    }

    [Fact]
    public void OutputFolderChangeRefreshesOutputPath()
    {
        using var temp = new TempDirectory();
        var inputFolder = Path.Combine(temp.Path, "input");
        var outputFolder = Path.Combine(temp.Path, "output");
        Directory.CreateDirectory(inputFolder);
        Directory.CreateDirectory(outputFolder);
        var inputPath = CreateFile(inputFolder, "clip.dat");
        var item = CreateReadyItem(inputPath, Path.Combine(inputFolder, "clip.mp4"), OutputFormat.Mp4);

        Refresh([item], CurrentSettings(
            OutputFormat.Mp4,
            outputDestinationMode: OutputDestinationMode.ChooseOutputFolder,
            chosenOutputFolder: outputFolder));

        Assert.Equal(OutputDestinationMode.ChooseOutputFolder, item.OutputDestinationMode);
        Assert.Equal(outputFolder, item.SelectedOutputFolder);
        Assert.Equal(Path.Combine(outputFolder, "clip.mp4"), item.PlannedOutputPath);
    }

    [Fact]
    public void CustomSaveAsPathSurvivesOutputFolderChange()
    {
        using var temp = new TempDirectory();
        var inputFolder = Path.Combine(temp.Path, "input");
        var outputFolder = Path.Combine(temp.Path, "output");
        var otherOutputFolder = Path.Combine(temp.Path, "other-output");
        Directory.CreateDirectory(inputFolder);
        Directory.CreateDirectory(outputFolder);
        Directory.CreateDirectory(otherOutputFolder);
        var inputPath = CreateFile(inputFolder, "clip.dat");
        var customOutputPath = Path.Combine(outputFolder, "front-door.mp4");
        var item = CreateReadyItem(inputPath, customOutputPath, OutputFormat.Mp4);
        item.CustomOutputPath = customOutputPath;

        Refresh([item], CurrentSettings(
            OutputFormat.Mp4,
            outputDestinationMode: OutputDestinationMode.ChooseOutputFolder,
            chosenOutputFolder: otherOutputFolder));

        Assert.Equal(customOutputPath, item.CustomOutputPath);
        Assert.Equal(customOutputPath, item.PlannedOutputPath);
        Assert.Equal(QueueItemStatus.Ready, item.Status);
    }

    [Fact]
    public void CustomSaveAsPathExtensionTracksFormatChange()
    {
        using var temp = new TempDirectory();
        var inputPath = CreateFile(temp.Path, "clip.dat");
        var customOutputPath = Path.Combine(temp.Path, "front-door.mp4");
        var item = CreateReadyItem(inputPath, customOutputPath, OutputFormat.Mp4);
        item.CustomOutputPath = customOutputPath;

        Refresh([item], CurrentSettings(OutputFormat.Mkv));

        Assert.Equal(Path.Combine(temp.Path, "front-door.mkv"), item.CustomOutputPath);
        Assert.Equal(Path.Combine(temp.Path, "front-door.mkv"), item.PlannedOutputPath);
        Assert.Equal(OutputFormat.Mkv, item.OutputFormat);
    }

    [Fact]
    public void MissingChosenOutputFolderMakesQueueItemInvalid()
    {
        using var temp = new TempDirectory();
        var inputPath = CreateFile(temp.Path, "clip.dat");
        var item = CreateReadyItem(inputPath, Path.Combine(temp.Path, "clip.mp4"), OutputFormat.Mp4);

        var result = Refresh([item], CurrentSettings(
            OutputFormat.Mp4,
            outputDestinationMode: OutputDestinationMode.ChooseOutputFolder,
            chosenOutputFolder: null));

        Assert.Equal(1, result.InvalidCount);
        Assert.Equal(QueueItemStatus.Invalid, item.Status);
        Assert.Equal("Output invalid", item.StatusText);
    }

    [Fact]
    public void RunningOrCompletedItemsAreNotMutatedByLiveRefresh()
    {
        using var temp = new TempDirectory();
        var inputPath = CreateFile(temp.Path, "clip.dat");
        var running = CreateReadyItem(inputPath, Path.Combine(temp.Path, "clip.mp4"), OutputFormat.Mp4);
        running.Status = QueueItemStatus.Converting;
        running.ConversionMode = "Remux";
        var completed = CreateReadyItem(inputPath, Path.Combine(temp.Path, "clip_01.mp4"), OutputFormat.Mp4);
        completed.Status = QueueItemStatus.Completed;

        var result = Refresh([running, completed], CurrentSettings(OutputFormat.Mkv, conversionMode: "Encode"));

        Assert.Equal(0, result.RefreshedCount);
        Assert.Equal(OutputFormat.Mp4, running.OutputFormat);
        Assert.Equal("Remux", running.ConversionMode);
        Assert.Equal(OutputFormat.Mp4, completed.OutputFormat);
    }

    private static QueueRefreshResult Refresh(IReadOnlyList<QueueItem> items, QueueSettingsSnapshot settings)
    {
        return QueueItemRefreshService.RefreshEditableItems(
            items,
            settings,
            (item, refreshSettings) => refreshSettings.OutputDestinationMode == OutputDestinationMode.SameFolderAsSource
                ? Path.GetDirectoryName(item.InputPath)
                : refreshSettings.ChosenOutputFolder,
            (item, outputFolderPath, outputFormat) => PlanQueueOutputPath(items, item, outputFolderPath, outputFormat),
            (item, outputFolderPath, outputFormat) => GetDirectOutputPath(item, outputFolderPath, outputFormat));
    }

    private static string? PlanQueueOutputPath(IReadOnlyList<QueueItem> items, QueueItem item, string outputFolderPath, OutputFormat outputFormat)
    {
        if (!string.IsNullOrWhiteSpace(item.CustomOutputPath))
        {
            var customValidation = OutputPathService.ValidateCustomOutputPath(
                item.InputPath,
                item.CustomOutputPath,
                outputFormat,
                requireAvailable: false,
                allowExtensionCorrection: true);
            if (!customValidation.IsValid || string.IsNullOrWhiteSpace(customValidation.OutputPath))
            {
                return null;
            }

            item.CustomOutputPath = customValidation.OutputPath;
            return IsAvailable(items, item, customValidation.OutputPath) || File.Exists(customValidation.OutputPath)
                ? customValidation.OutputPath
                : null;
        }

        var directOutputPath = OutputPathService.GetDirectOutputPath(item.InputPath, outputFolderPath, outputFormat);
        if (string.IsNullOrWhiteSpace(directOutputPath))
        {
            return null;
        }

        if (IsAvailable(items, item, directOutputPath))
        {
            return directOutputPath;
        }

        return OutputPathService.IsSafeOutputPath(item.InputPath, directOutputPath) ? directOutputPath : null;
    }

    private static string? GetDirectOutputPath(QueueItem item, string outputFolderPath, OutputFormat outputFormat)
    {
        if (!string.IsNullOrWhiteSpace(item.CustomOutputPath))
        {
            var customValidation = OutputPathService.ValidateCustomOutputPath(
                item.InputPath,
                item.CustomOutputPath,
                outputFormat,
                requireAvailable: false,
                allowExtensionCorrection: true);
            return customValidation.IsValid ? customValidation.OutputPath : null;
        }

        return OutputPathService.GetDirectOutputPath(item.InputPath, outputFolderPath, outputFormat);
    }

    private static bool IsAvailable(IReadOnlyList<QueueItem> items, QueueItem item, string outputPath)
    {
        return OutputPathService.IsSafeOutputPath(item.InputPath, outputPath) &&
               !File.Exists(outputPath) &&
               !items.Any(other => other != item && string.Equals(other.PlannedOutputPath, outputPath, StringComparison.OrdinalIgnoreCase));
    }

    private static QueueItem CreateReadyItem(string inputPath, string outputPath, OutputFormat outputFormat, bool hasExistingDirectOutput = false)
    {
        return new QueueItem(
            inputPath,
            outputPath,
            OutputDestinationMode.SameFolderAsSource,
            null,
            outputFormat,
            "Remux",
            FpsOption.FromLabel("30"),
            hasExistingDirectOutput)
        {
            PreProbeResult = new ProbeResult(true, "ok", "ffprobe", FpsOption.FromLabel("30"), Width: 1920, Height: 1080),
            Status = hasExistingDirectOutput ? QueueItemStatus.Skipped : QueueItemStatus.Ready,
            StatusText = hasExistingDirectOutput ? "Exists" : "Ready",
            ProgressText = hasExistingDirectOutput ? "Selected output exists" : "1920x1080"
        };
    }

    private static QueueSettingsSnapshot CurrentSettings(
        OutputFormat outputFormat,
        string conversionMode = "Remux",
        FpsOption? fps = null,
        OutputDestinationMode outputDestinationMode = OutputDestinationMode.SameFolderAsSource,
        string? chosenOutputFolder = null)
    {
        return new QueueSettingsSnapshot(
            outputFormat,
            conversionMode,
            fps ?? FpsOption.FromLabel("30"),
            outputDestinationMode,
            chosenOutputFolder);
    }

    private static string CreateFile(string folderPath, string fileName)
    {
        Directory.CreateDirectory(folderPath);
        var path = Path.Combine(folderPath, fileName);
        File.WriteAllText(path, "content");
        return path;
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "DatConverter.Tests." + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch
            {
            }
        }
    }
}
