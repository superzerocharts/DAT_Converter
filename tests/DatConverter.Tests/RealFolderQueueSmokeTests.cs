namespace DatConverter.Tests;

public sealed class RealFolderQueueSmokeTests
{
    [Theory]
    [InlineData(OutputFormat.Mp4, "Remux", FpsSelectionMode.AutoDetect)]
    [InlineData(OutputFormat.Mp4, "Encode", FpsSelectionMode.AutoDetect)]
    [InlineData(OutputFormat.Mkv, "Remux", FpsSelectionMode.AutoDetect)]
    [InlineData(OutputFormat.Mkv, "Encode", FpsSelectionMode.AutoDetect)]
    [InlineData(OutputFormat.Mp4, "Remux", FpsSelectionMode.Manual)]
    [InlineData(OutputFormat.Mp4, "Encode", FpsSelectionMode.Manual)]
    [InlineData(OutputFormat.Mkv, "Remux", FpsSelectionMode.Manual)]
    [InlineData(OutputFormat.Mkv, "Encode", FpsSelectionMode.Manual)]
    public async Task Camera205VideoFiles_AllGlobalSettingCombinations_ReachExpectedReadinessAndCommands(
        OutputFormat outputFormat,
        string conversionMode,
        FpsSelectionMode fpsSelectionMode)
    {
        var folderPath = @"W:\Projects\Camera 205 Sample";
        if (!Directory.Exists(folderPath))
        {
            return;
        }

        var paths = GetRequiredVideoSamplePaths(folderPath);
        var tools = ToolPathService.ResolveBundledTools();
        Assert.True(tools.AreAvailable, $"Bundled FFmpeg tools are missing: {tools.FfmpegPath}; {tools.FfprobePath}");
        var resolver = new QueueItemFpsResolver();
        var probeService = new ProbeService(tools);
        var fpsSettings = fpsSelectionMode == FpsSelectionMode.AutoDetect
            ? QueueItemFpsSettings.AutoDetect()
            : QueueItemFpsSettings.FromManual(FpsOption.FromLabel("30"));

        var items = paths
            .Select(path => CreateQueueItem(path, outputFormat, conversionMode, fpsSettings, resolver.ResolveQueueItemFps(path, fpsSettings)))
            .ToList();

        await ProbeAllEligibleItems(items, probeService);

        var normal = Find(items, "dvrfile00000001.dat");
        var datOnly = Find(items, "mirasys_5min_sample_2.dat");
        var corrupt = Find(items, "dvrfile00000001_corrupt_fps_timestamps.dat");

        AssertReadyWithExpectedSettings(normal, outputFormat, conversionMode);
        AssertReadyWithExpectedSettings(datOnly, outputFormat, conversionMode);
        if (fpsSelectionMode == FpsSelectionMode.AutoDetect)
        {
            Assert.Equal("Auto 30", normal.FpsDisplayLabel);
            Assert.Equal("Auto 30", datOnly.FpsDisplayLabel);
            Assert.Equal(QueueItemStatus.Warning, corrupt.Status);
            Assert.Equal("Needs FPS", corrupt.StatusText);
            Assert.True(corrupt.RequiresManualFpsSelection);

            var blocked = QueueFpsValidationService.FindItemsRequiringManualFps(items);
            var blockedItem = Assert.Single(blocked);
            Assert.Same(corrupt, blockedItem);

            corrupt.ApplyFpsResolution(
                QueueItemFpsSettings.FromManual(FpsOption.FromLabel("30")),
                QueueItemFpsResolution.FromManual(FpsOption.FromLabel("30")));
            if (QueueItemStatusService.HasReusableProbeForCurrentFps(corrupt))
            {
                QueueItemStatusService.ApplyPreProbeResult(corrupt, corrupt.PreProbeResult!);
            }
            else
            {
                corrupt.Status = QueueItemStatus.WaitingForProbe;
                corrupt.StatusText = QueueItemStatusText.CheckingFile;
                await ProbeAllEligibleItems([corrupt], probeService);
            }
        }

        AssertReadyWithExpectedSettings(corrupt, outputFormat, conversionMode);
        Assert.Empty(QueueFpsValidationService.FindItemsRequiringManualFps(items));

        foreach (var item in items)
        {
            var arguments = string.Equals(conversionMode, "Encode", StringComparison.OrdinalIgnoreCase)
                ? FfmpegCommandBuilder.BuildEncodeArguments(item.InputPath, item.PlannedOutputPath, outputFormat, item.Fps)
                : FfmpegCommandBuilder.BuildRemuxArguments(item.InputPath, item.PlannedOutputPath, outputFormat, item.Fps);

            Assert.Equal("30", GetOptionValue(arguments, "-r"));
            Assert.EndsWith(outputFormat.Extension(), item.PlannedOutputPath, StringComparison.OrdinalIgnoreCase);
            if (string.Equals(conversionMode, "Encode", StringComparison.OrdinalIgnoreCase))
            {
                Assert.Contains("fps=30", GetOptionValue(arguments, "-vf"));
            }
            else
            {
                Assert.DoesNotContain("-vf", arguments);
            }
        }
    }

    [Fact]
    public async Task Camera205Folder_AddWithSubfolders_ProbesAndClassifiesEveryDat()
    {
        var folderPath = @"W:\Projects\Camera 205 Sample";
        if (!Directory.Exists(folderPath))
        {
            return;
        }

        var scan = FolderScanService.ScanForDatFiles(folderPath, includeSubfolders: true, hardLimit: 100);
        Assert.False(scan.StoppedBecauseTooManyFiles);
        Assert.Contains(scan.DatFiles, path => Path.GetFileName(path).Equals("dvrfile00000001.dat", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(scan.DatFiles, path => Path.GetFileName(path).Equals("dvrfile00000001_corrupt_fps_timestamps.dat", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(scan.DatFiles, path => Path.GetFileName(path).Equals("mirasys_5min_sample_2.dat", StringComparison.OrdinalIgnoreCase));

        var tools = ToolPathService.ResolveBundledTools();
        Assert.True(tools.AreAvailable, $"Bundled FFmpeg tools are missing: {tools.FfmpegPath}; {tools.FfprobePath}");
        var resolver = new QueueItemFpsResolver();
        var probeService = new ProbeService(tools);
        var items = scan.DatFiles
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => CreateQueueItem(path, OutputFormat.Mp4, "Remux", QueueItemFpsSettings.AutoDetect(), resolver.ResolveQueueItemFps(path, QueueItemFpsSettings.AutoDetect())))
            .ToList();

        await ProbeAllEligibleItems(items, probeService);

        var normal = Find(items, "dvrfile00000001.dat");
        Assert.Equal("Auto 30", normal.FpsDisplayLabel);
        Assert.Equal("30", normal.FfmpegRateValue);
        Assert.Equal(QueueItemStatus.Ready, normal.Status);
        Assert.Equal("Ready", normal.StatusText);

        var datOnly = Find(items, "mirasys_5min_sample_2.dat");
        Assert.Equal("Auto 30", datOnly.FpsDisplayLabel);
        Assert.Equal("30", datOnly.FfmpegRateValue);
        Assert.Equal(QueueItemStatus.Ready, datOnly.Status);

        var corrupt = Find(items, "dvrfile00000001_corrupt_fps_timestamps.dat");
        Assert.False(corrupt.HasResolvedFps);
        Assert.True(corrupt.RequiresManualFpsSelection);
        Assert.NotNull(corrupt.PreProbeResult);
        Assert.True(corrupt.PreProbeResult!.IsSuccess, corrupt.PreProbeResult.TechnicalDetails);
        Assert.Equal(QueueItemStatus.Warning, corrupt.Status);
        Assert.Equal("Needs FPS", corrupt.StatusText);
        Assert.Equal("Choose Source FPS", corrupt.ProgressText);

        var indexFile = Find(items, "MaterialFolderIndex.dat");
        Assert.Equal(QueueItemStatus.Unsupported, indexFile.Status);
        Assert.Equal("Unsupported", indexFile.StatusText);

        var blocked = QueueFpsValidationService.FindItemsRequiringManualFps(items);
        var blockedItem = Assert.Single(blocked);
        Assert.Same(corrupt, blockedItem);
        Assert.Contains("dvrfile00000001_corrupt_fps_timestamps.dat", QueueFpsValidationService.BuildManualFpsRequiredMessage(blocked));

        corrupt.ApplyFpsResolution(
            QueueItemFpsSettings.FromManual(FpsOption.FromLabel("30")),
            QueueItemFpsResolution.FromManual(FpsOption.FromLabel("30")));
        Assert.True(QueueItemStatusService.HasReusableProbeForCurrentFps(corrupt));
        QueueItemStatusService.ApplyPreProbeResult(corrupt, corrupt.PreProbeResult!);

        Assert.Equal("30", corrupt.FpsDisplayLabel);
        Assert.Equal("30", corrupt.FfmpegRateValue);
        Assert.Equal(QueueItemStatus.Ready, corrupt.Status);
        Assert.Empty(QueueFpsValidationService.FindItemsRequiringManualFps(items));
    }

    private static IReadOnlyList<string> GetRequiredVideoSamplePaths(string folderPath)
    {
        var scan = FolderScanService.ScanForDatFiles(folderPath, includeSubfolders: true, hardLimit: 100);
        Assert.False(scan.StoppedBecauseTooManyFiles);

        return new[]
        {
            RequirePath(scan.DatFiles, "dvrfile00000001.dat"),
            RequirePath(scan.DatFiles, "mirasys_5min_sample_2.dat"),
            RequirePath(scan.DatFiles, "dvrfile00000001_corrupt_fps_timestamps.dat")
        };
    }

    private static QueueItem CreateQueueItem(
        string inputPath,
        OutputFormat outputFormat,
        string conversionMode,
        QueueItemFpsSettings fpsSettings,
        QueueItemFpsResolution fpsResolution)
    {
        var outputPath = Path.ChangeExtension(inputPath, outputFormat.Extension());
        var item = new QueueItem(
            inputPath,
            outputPath,
            OutputDestinationMode.SameFolderAsSource,
            null,
            outputFormat,
            conversionMode,
            FpsOption.FromLabel("30"),
            hasExistingDirectOutput: false);
        item.ApplyFpsResolution(fpsSettings, fpsResolution);
        QueueItemStatusService.ApplyPostFpsResolutionStatus(item);
        return item;
    }

    private static async Task ProbeAllEligibleItems(IEnumerable<QueueItem> items, ProbeService probeService)
    {
        foreach (var item in items.Where(QueuePreProbeService.ShouldPreProbe).ToList())
        {
            var probeFps = item.HasResolvedFps ? item.Fps : FpsOption.FromLabel("30");
            var probeResult = await probeService.ProbeRawH264Async(item.InputPath, probeFps, CancellationToken.None);
            QueueItemStatusService.ApplyPreProbeResult(item, probeResult);
        }
    }

    private static string RequirePath(IEnumerable<string> paths, string fileName)
    {
        return paths.Single(path => Path.GetFileName(path).Equals(fileName, StringComparison.OrdinalIgnoreCase));
    }

    private static QueueItem Find(IEnumerable<QueueItem> items, string fileName)
    {
        return items.Single(item => Path.GetFileName(item.InputPath).Equals(fileName, StringComparison.OrdinalIgnoreCase));
    }

    private static void AssertReadyWithExpectedSettings(QueueItem item, OutputFormat outputFormat, string conversionMode)
    {
        Assert.Equal(QueueItemStatus.Ready, item.Status);
        Assert.Equal("Ready", item.StatusText);
        Assert.Equal(outputFormat, item.OutputFormat);
        Assert.Equal(conversionMode, item.ConversionMode);
        Assert.Equal("30", item.FfmpegRateValue);
        Assert.False(item.RequiresManualFpsSelection);
        Assert.True(item.HasResolvedFps);
        Assert.NotNull(item.PreProbeResult);
        Assert.True(item.PreProbeResult!.IsSuccess, item.PreProbeResult.TechnicalDetails);
    }

    private static string GetOptionValue(IReadOnlyList<string> arguments, string option)
    {
        var index = arguments.ToList().IndexOf(option);
        Assert.True(index >= 0 && index < arguments.Count - 1, $"Missing option value for {option}.");
        return arguments[index + 1];
    }
}
