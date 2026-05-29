namespace DatConverter.Tests;

public sealed class AppSettingsServiceTests
{
    [Fact]
    public void CreateDefault_UsesAutoDetectFps()
    {
        var settings = AppSettingsService.CreateDefault();

        Assert.Equal("Auto-detect", settings.Fps);
    }

    [Fact]
    public void CreateDefault_UsesComfortableMainWindowSize()
    {
        var settings = AppSettingsService.CreateDefault();

        Assert.Equal(1080, settings.WindowWidth);
        Assert.Equal(740, settings.WindowHeight);
    }

    [Fact]
    public void Normalize_PreservesAutoDetectFps()
    {
        var settings = AppSettingsService.Normalize(new AppSettings { Fps = "Auto-detect" });

        Assert.Equal("Auto-detect", settings.Fps);
    }

    [Theory]
    [InlineData("15", "15")]
    [InlineData("20", "20")]
    [InlineData("24", "24")]
    [InlineData("25", "25")]
    [InlineData("29.97", "29.97")]
    [InlineData("30", "30")]
    public void Normalize_PreservesManualFpsChoices(string value, string expected)
    {
        var settings = AppSettingsService.Normalize(new AppSettings { Fps = value });

        Assert.Equal(expected, settings.Fps);
    }

    [Theory]
    [InlineData("Fast", "Remux", "Fast")]
    [InlineData("Full", "Encode", "Full")]
    [InlineData("Full NVENC", "EncodeNvenc", "Full NVENC")]
    [InlineData("EncodeNvenc", "EncodeNvenc", "Full NVENC")]
    public void ConversionModes_ParseAndFormatDisplayNames(string display, string expectedInternalMode, string expectedDisplay)
    {
        var parsed = ConversionModes.ParseDisplay(display);

        Assert.Equal(expectedInternalMode, parsed);
        Assert.Equal(expectedDisplay, ConversionModes.FormatDisplay(parsed));
    }

    [Fact]
    public void Normalize_FullNvencUnavailableFallsBackToFull()
    {
        var settings = AppSettingsService.Normalize(new AppSettings { ConversionMode = "Full NVENC" }, nvencAvailable: false);

        Assert.Equal(ConversionModes.Encode, settings.ConversionMode);
    }

    [Fact]
    public void Normalize_FullNvencAvailablePreservesNvenc()
    {
        var settings = AppSettingsService.Normalize(new AppSettings { ConversionMode = "Full NVENC" }, nvencAvailable: true);

        Assert.Equal(ConversionModes.EncodeNvenc, settings.ConversionMode);
    }

    [Theory]
    [InlineData("Auto-detect")]
    [InlineData("25")]
    [InlineData("29.97")]
    public void Load_ResetsSavedFpsToStartupAutoDetectDefault(string savedFps)
    {
        using var temp = new TempDirectory();
        var settingsPath = Path.Combine(temp.Path, "settings.json");
        var service = new AppSettingsService(settingsPath);

        var saved = service.Save(new AppSettings { Fps = savedFps }, out var errorMessage);
        var loaded = service.Load(out _);

        Assert.True(saved, errorMessage);
        Assert.Equal("Auto-detect", loaded.Fps);
    }

    [Fact]
    public void Normalize_ClampsMainWindowMinimumSize()
    {
        var settings = AppSettingsService.Normalize(new AppSettings { WindowWidth = 100, WindowHeight = 100 });

        Assert.Equal(960, settings.WindowWidth);
        Assert.Equal(680, settings.WindowHeight);
    }

    [Fact]
    public void MainForm_ClampsStartupWindowSizeToWorkingAreaWithMargin()
    {
        var size = MainForm.GetClampedStartupWindowSize(
            new System.Drawing.Size(1800, 1000),
            new System.Drawing.Rectangle(0, 0, 1536, 824));

        Assert.Equal(1080, size.Width);
        Assert.Equal(744, size.Height);
    }

    [Fact]
    public void MainForm_ClampsStartupWindowSizeForScaledWorkingArea()
    {
        var size = MainForm.GetClampedStartupWindowSize(
            new System.Drawing.Size(1800, 1000),
            new System.Drawing.Rectangle(0, 0, 1536, 824));

        Assert.True(size.Width <= 1536 - 80);
        Assert.True(size.Height <= 824 - 80);
    }

    [Fact]
    public void MainForm_CanRemoveQueueItemForState_AllowsSelectedPendingItemOnlyWhenQueueIsIdle()
    {
        var item = CreateItem();

        Assert.True(MainForm.CanRemoveQueueItemForState(item, currentQueueItem: null, isQueueProcessing: false));
        Assert.False(MainForm.CanRemoveQueueItemForState(item, currentQueueItem: null, isQueueProcessing: true));

        item.Status = QueueItemStatus.Converting;

        Assert.False(MainForm.CanRemoveQueueItemForState(item, currentQueueItem: item, isQueueProcessing: false));
    }

    [Fact]
    public void MainForm_RemoveSelectedQueueItemsForState_RemovesOnlySelectedItems()
    {
        var first = CreateItem("first.dat");
        var second = CreateItem("second.dat");
        var third = CreateItem("third.dat");
        var queue = new List<QueueItem> { first, second, third };

        var removed = MainForm.RemoveSelectedQueueItemsForState(
            queue,
            [second],
            currentQueueItem: null,
            isQueueProcessing: false);

        Assert.Equal(1, removed);
        Assert.Equal([first, third], queue);
    }

    [Fact]
    public void MainForm_RemoveSelectedQueueItemsForState_DoesNotRemoveWhileQueueRunning()
    {
        var first = CreateItem("first.dat");
        var second = CreateItem("second.dat");
        var queue = new List<QueueItem> { first, second };

        var removed = MainForm.RemoveSelectedQueueItemsForState(
            queue,
            [first],
            currentQueueItem: first,
            isQueueProcessing: true);

        Assert.Equal(0, removed);
        Assert.Equal([first, second], queue);
    }

    [Fact]
    public void MainForm_CopyActionText_UsesDuplicateForReadyAndReAddForCompleted()
    {
        var item = CreateItem();

        Assert.Equal("Duplicate", MainForm.GetQueueCopyActionText(item));

        item.Status = QueueItemStatus.Completed;

        Assert.Equal("Re-add", MainForm.GetQueueCopyActionText(item));
    }

    [Fact]
    public void MainForm_CanCopyQueueItemForState_DisablesWhileRunning()
    {
        var item = CreateItem();

        Assert.True(MainForm.CanCopyQueueItemForState(item, isQueueProcessing: false));
        Assert.False(MainForm.CanCopyQueueItemForState(item, isQueueProcessing: true));

        item.Status = QueueItemStatus.Converting;

        Assert.False(MainForm.CanCopyQueueItemForState(item, isQueueProcessing: false));
    }

    [Fact]
    public void QueueItemCopyService_CreateReadyCopy_ResetsToFreshQueueDefaults()
    {
        var source = CreateItem("source.dat");
        source.OutputFormat = OutputFormat.Mkv;
        source.ConversionMode = "Encode";
        source.TrimRange = new TrimRange(TimeSpan.FromSeconds(32), TimeSpan.FromSeconds(107));
        source.HasCustomFormat = true;
        source.HasCustomMode = true;
        source.HasCustomFpsSetting = true;
        source.Status = QueueItemStatus.Completed;
        source.StatusText = "Completed";
        source.ProgressText = "100%";
        source.ResultStatusSummary = "Completed";

        var copy = QueueItemCopyService.CreateReadyCopy(
            source,
            Path.ChangeExtension(source.PlannedOutputPath, ".copy.mkv"),
            OutputDestinationMode.SameFolderAsSource,
            selectedOutputFolder: null,
            OutputFormat.Mp4,
            AutoFpsResolution(),
            QueueItemFpsSettings.AutoDetect());

        Assert.Equal(source.InputPath, copy.InputPath);
        Assert.Equal(OutputFormat.Mp4, copy.OutputFormat);
        Assert.Equal("Fast", copy.ConversionMode);
        Assert.Null(copy.TrimRange);
        Assert.Equal(FpsSelectionMode.AutoDetect, copy.FpsSelectionMode);
        Assert.False(copy.HasCustomFormat);
        Assert.False(copy.HasCustomMode);
        Assert.False(copy.HasCustomFpsSetting);
        Assert.Equal(QueueItemStatus.Ready, copy.Status);
        Assert.Equal("Ready", copy.StatusText);
        Assert.Equal("Ready", copy.ProgressText);
        Assert.Null(copy.ResultStatusSummary);
        Assert.Null(copy.ConversionResult);
    }

    [Fact]
    public void QueueItemCopyService_CreateReadyCopy_ResetsCompletedItemToReady()
    {
        var source = CreateItem("completed.dat");
        source.Status = QueueItemStatus.Completed;
        source.StatusText = "Completed";
        source.ProgressText = "100%";

        var copy = QueueItemCopyService.CreateReadyCopy(
            source,
            Path.ChangeExtension(source.PlannedOutputPath, ".copy.mp4"),
            OutputDestinationMode.SameFolderAsSource,
            selectedOutputFolder: null,
            OutputFormat.Mp4,
            AutoFpsResolution(),
            QueueItemFpsSettings.AutoDetect());

        Assert.Equal(QueueItemStatus.Ready, copy.Status);
        Assert.Equal("Ready", copy.StatusText);
        Assert.Equal("Ready", copy.ProgressText);
        Assert.Equal(QueueItemStatus.Completed, source.Status);
    }

    [Fact]
    public void QueueItemCopyService_CreateReadyCopy_PreservesSplitLogicalBaseName()
    {
        var source = CreateSplitItem("Cam 8379 - 4 hr clip");

        var copy = QueueItemCopyService.CreateReadyCopy(
            source,
            Path.Combine(Path.GetDirectoryName(source.PlannedOutputPath)!, "Cam 8379 - 4 hr clip_01.mp4"),
            OutputDestinationMode.SameFolderAsSource,
            selectedOutputFolder: null,
            OutputFormat.Mp4,
            AutoFpsResolution(),
            QueueItemFpsSettings.AutoDetect());

        Assert.True(copy.IsSplitRecording);
        Assert.Equal("Cam 8379 - 4 hr clip", copy.LogicalOutputBaseName);
        Assert.Null(copy.TrimRange);
    }

    [Fact]
    public void MainForm_ResolveLogicalOutputBaseName_RemovesDuplicateSuffixForSplitRecording()
    {
        var item = CreateSplitItem(logicalBaseName: null);
        item.PlannedOutputPath = Path.Combine(Path.GetDirectoryName(item.PlannedOutputPath)!, "Cam 8379 - 4 hr clip_01.mp4");

        Assert.Equal("Cam 8379 - 4 hr clip", MainForm.ResolveLogicalOutputBaseName(item));
    }

    [Fact]
    public void MainForm_ResolveLogicalOutputBaseName_RemovesTrimSuffixForSplitRecording()
    {
        var item = CreateSplitItem(logicalBaseName: null);
        item.TrimRange = new TrimRange(TimeSpan.Zero, TimeSpan.FromMinutes(12));
        item.PlannedOutputPath = Path.Combine(Path.GetDirectoryName(item.PlannedOutputPath)!, "Cam 8379 - 4 hr clip_trim_260522_0541-260522_0553_01.mp4");

        Assert.Equal("Cam 8379 - 4 hr clip", MainForm.ResolveLogicalOutputBaseName(item));
    }

    private static QueueItem CreateItem(string fileName = "sample.dat")
    {
        var root = Path.Combine(Path.GetTempPath(), "DatConverter.Tests");
        return new QueueItem(
            Path.Combine(root, fileName),
            Path.ChangeExtension(Path.Combine(root, fileName), ".mp4"),
            OutputDestinationMode.SameFolderAsSource,
            null,
            OutputFormat.Mp4,
            "Remux",
            FpsOption.FromLabel("30"),
            hasExistingDirectOutput: false);
    }

    private static QueueItem CreateSplitItem(string? logicalBaseName)
    {
        var root = Path.Combine(Path.GetTempPath(), "DatConverter.Tests", "Cam 8379 - 4 hr clip");
        var inputPath = Path.Combine(root, "dvrfile00000001.dat");
        var item = new QueueItem(
            inputPath,
            Path.Combine(root, "Cam 8379 - 4 hr clip.mp4"),
            OutputDestinationMode.SameFolderAsSource,
            null,
            OutputFormat.Mp4,
            "Fast",
            FpsOption.FromLabel("30"),
            hasExistingDirectOutput: false)
        {
            LogicalOutputBaseName = logicalBaseName,
            SplitExportPlan = new SpotterSplitExportPlan
            {
                ExportFolder = root,
                Confidence = "Strong",
                Segments =
                [
                    new SpotterSplitExportSegment
                    {
                        SegmentNumber = 1,
                        FileName = "dvrfile00000001.dat",
                        FilePath = inputPath,
                        StartTime = new DateTime(2026, 5, 22, 5, 41, 0),
                        EndTime = new DateTime(2026, 5, 22, 5, 53, 0)
                    },
                    new SpotterSplitExportSegment
                    {
                        SegmentNumber = 2,
                        FileName = "dvrfile00000002.dat",
                        FilePath = Path.Combine(root, "dvrfile00000002.dat"),
                        StartTime = new DateTime(2026, 5, 22, 5, 53, 0),
                        EndTime = new DateTime(2026, 5, 22, 6, 5, 0)
                    }
                ]
            }
        };
        return item;
    }

    private static QueueItemFpsResolution AutoFpsResolution()
    {
        return new QueueItemFpsResolution
        {
            SelectionMode = FpsSelectionMode.AutoDetect,
            DisplayLabel = "30",
            FfmpegRateValue = "30",
            NominalConversionFps = 30,
            HasResolvedFps = true,
            AutoDetectionSucceeded = true,
            Confidence = "High",
            DecisionReason = "Detected from test records."
        };
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
