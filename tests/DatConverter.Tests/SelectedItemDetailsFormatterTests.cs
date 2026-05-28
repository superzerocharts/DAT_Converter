namespace DatConverter.Tests;

public sealed class SelectedItemDetailsFormatterTests
{
    private static readonly string[] ExpectedLabels =
    [
        "Input file",
        "Planned output file",
        "Output format",
        "Mode",
        "Selected source FPS",
        "FFmpeg FPS value",
        "FPS confidence",
        "FPS note",
        "Source type",
        "Parts",
        "Export segment",
        "Split recording segments",
        "Trim",
        "Probe status",
        "Conversion status",
        "Duration available",
        "Duration value",
        "Progress mode",
        "Status",
        "Output",
        "Exit code",
        "Canceled",
        "Timed out"
    ];

    [Fact]
    public void BuildLines_NoSelectedItem_UsesFixedFieldOrderWithBlankValues()
    {
        var lines = SelectedItemDetailsFormatter.BuildLines(null);

        Assert.Equal(ExpectedLabels, ExtractLabels(lines));
        Assert.All(lines, line => Assert.EndsWith(":", line));
    }

    [Fact]
    public void BuildLines_CompletedItem_UsesFixedFieldsAndCompletionValues()
    {
        var item = CreateItem("sample.dat", "sample.mp4");
        item.Status = QueueItemStatus.Completed;
        item.StatusText = "Completed";
        item.PreProbeResult = new ProbeResult(
            true,
            "OK",
            "ffprobe.exe",
            item.Fps,
            Duration: "300.5");
        item.ConversionResult = new ConversionResult(
            true,
            "Completed.",
            "ffmpeg.exe",
            Array.Empty<string>(),
            item.InputPath,
            item.PlannedOutputPath,
            item.Fps,
            0,
            "",
            "",
            ConversionMode: item.ConversionMode,
            OutputFormat: item.OutputFormat.DisplayName(),
            Duration: TimeSpan.FromSeconds(300.5),
            UsedDeterminateProgress: true,
            ProcessingTime: TimeSpan.FromSeconds(0.54));

        var lines = SelectedItemDetailsFormatter.BuildLines(item);

        Assert.Equal(ExpectedLabels, ExtractLabels(lines));
        Assert.Contains("Mode: Fast", lines);
        Assert.Contains("Probe status: Succeeded", lines);
        Assert.Contains("Conversion status: Completed", lines);
        Assert.Contains("Duration available: Yes", lines);
        Assert.Contains("Duration value: 05:00", lines);
        Assert.Contains("Progress mode: Determinate", lines);
        Assert.Contains("Status: Completed in 0.5 seconds", lines);
        Assert.Contains($"Output: {item.PlannedOutputPath}", lines);
        Assert.Contains("Exit code: 0", lines);
        Assert.Contains("Canceled: No", lines);
        Assert.Contains("Timed out: No", lines);
    }

    [Fact]
    public void BuildLines_UnsupportedItem_UsesFixedFieldsWithBlankExitCode()
    {
        var item = CreateItem("helper.dat", "helper.mp4");
        item.Status = QueueItemStatus.Unsupported;
        item.StatusText = "Unsupported";
        item.PreProbeResult = new ProbeResult(
            false,
            ProbeResult.UnsupportedMessage,
            "ffprobe.exe",
            item.Fps);
        item.ResultStatusSummary = "Skipped - unsupported video payload";

        var lines = SelectedItemDetailsFormatter.BuildLines(item);

        Assert.Equal(ExpectedLabels, ExtractLabels(lines));
        Assert.Contains("Probe status: Failed", lines);
        Assert.Contains("Conversion status: Skipped", lines);
        Assert.Contains("Duration available: No", lines);
        Assert.Contains("Duration value: Unknown", lines);
        Assert.Contains("Progress mode: Indeterminate", lines);
        Assert.Contains("Status: Skipped - unsupported video payload", lines);
        Assert.Contains("Output: not created", lines);
        Assert.Contains("Exit code:", lines);
        Assert.Contains("Canceled: No", lines);
        Assert.Contains("Timed out: No", lines);
    }

    [Fact]
    public void BuildLines_AutoDetectedItem_ShowsConciseFpsDetails()
    {
        var item = CreateItem("sample.dat", "sample.mp4");
        item.ApplyFpsResolution(new QueueItemFpsResolution
        {
            SelectionMode = FpsSelectionMode.AutoDetect,
            DisplayLabel = "Auto 30",
            FfmpegRateValue = "30",
            NominalConversionFps = 30,
            AutoDetectionSucceeded = true,
            Confidence = "High",
            DecisionReason = "Detected from Spotter frame records.",
            TechnicalLogText = "Average FPS: 29.900\r\nPer-second FPS: median=30"
        });

        var lines = SelectedItemDetailsFormatter.BuildLines(item);

        Assert.Contains("Selected source FPS: Auto 30", lines);
        Assert.Contains("FFmpeg FPS value: 30", lines);
        Assert.Contains("FPS confidence: High", lines);
        Assert.Contains("FPS note: Detected from Spotter frame records.", lines);
        Assert.DoesNotContain(lines, line => line.Contains("29.900", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.Contains("Per-second FPS", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildLines_MultiFileExportContext_ShowsSimpleSegmentNote()
    {
        var item = CreateItem("dvrfile00000002.dat", "dvrfile00000002.mp4");
        item.MultiFileExportContext = new SpotterMultiFileExportContext
        {
            SidecarPath = Path.Combine(Path.GetTempPath(), "sample.sef2"),
            SegmentNumber = 2,
            SegmentCount = 4,
            SegmentFileNames =
            [
                "dvrfile00000001.dat",
                "dvrfile00000002.dat",
                "dvrfile00000003.dat",
                "dvrfile00000004.dat"
            ]
        };

        var lines = SelectedItemDetailsFormatter.BuildLines(item);

        Assert.Contains("Export segment: Multi-file export detected: segment 2 of 4.", lines);
    }

    [Fact]
    public void BuildLines_SplitRecording_ShowsPartsAndSegmentList()
    {
        var item = CreateItem("dvrfile00000001.dat", "export.mp4");
        item.SplitExportPlan = new SpotterSplitExportPlan
        {
            ExportFolder = Path.GetTempPath(),
            Confidence = "Strong",
            Segments =
            [
                new SpotterSplitExportSegment { SegmentNumber = 1, FileName = "dvrfile00000001.dat", FilePath = item.InputPath },
                new SpotterSplitExportSegment { SegmentNumber = 2, FileName = "dvrfile00000002.dat", FilePath = Path.Combine(Path.GetTempPath(), "dvrfile00000002.dat") }
            ]
        };

        var lines = SelectedItemDetailsFormatter.BuildLines(item);

        Assert.Contains("Source type: Split recording", lines);
        Assert.Contains("Parts: 2", lines);
        Assert.Contains("Split recording segments: dvrfile00000001.dat, dvrfile00000002.dat", lines);
    }

    [Fact]
    public void BuildLines_UnresolvedAutoFpsItem_ShowsManualSelectionRequired()
    {
        var item = CreateItem("sample.dat", "sample.mp4");
        item.ApplyFpsResolution(new QueueItemFpsResolution
        {
            SelectionMode = FpsSelectionMode.AutoDetect,
            DisplayLabel = "Needs manual selection",
            FfmpegRateValue = "",
            NominalConversionFps = null,
            HasResolvedFps = false,
            RequiresManualFpsSelection = true,
            FpsValidationMessage = "Auto-detect could not determine the source FPS. Double-click this row and choose Source FPS.",
            AutoDetectionSucceeded = false,
            Confidence = "Unavailable",
            Warning = "FPS auto-detection failed.",
            TechnicalLogText = "failure details"
        });

        var lines = SelectedItemDetailsFormatter.BuildLines(item);

        Assert.Contains("Selected source FPS: Needs manual selection", lines);
        Assert.Contains("FFmpeg FPS value: Not set", lines);
        Assert.Contains("FPS confidence: Unavailable", lines);
        Assert.Contains("FPS note: Auto-detect could not determine the source FPS. Double-click this row and choose Source FPS.", lines);
    }

    private static QueueItem CreateItem(string inputFileName, string outputFileName)
    {
        var root = Path.Combine(Path.GetTempPath(), "DatConverter.Tests");
        return new QueueItem(
            Path.Combine(root, inputFileName),
            Path.Combine(root, outputFileName),
            OutputDestinationMode.SameFolderAsSource,
            null,
            OutputFormat.Mp4,
            "Fast",
            FpsOption.FromLabel("30"),
            hasExistingDirectOutput: false);
    }

    private static string[] ExtractLabels(IReadOnlyList<string> lines)
    {
        return lines
            .Select(line => line.Split(':')[0])
            .ToArray();
    }
}
