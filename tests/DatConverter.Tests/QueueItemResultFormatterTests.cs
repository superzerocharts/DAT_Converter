namespace DatConverter.Tests;

public sealed class QueueItemResultFormatterTests
{
    [Fact]
    public void BuildLogLine_IncludesCompletedItemNumberFileTimeAndOutput()
    {
        var item = CreateItem("sample_2.dat", "sample_2.mp4");
        item.Status = QueueItemStatus.Completed;
        item.StatusText = "Completed";
        item.ConversionResult = CreateResult(
            item,
            isSuccess: true,
            processingTime: TimeSpan.FromSeconds(0.54),
            exitCode: 0);

        var line = QueueItemResultFormatter.BuildLogLine(item, 1, 3);

        Assert.Equal($"Queue item result: 1 of 3; sample_2.dat; Completed in 0.5 seconds; Output: {item.PlannedOutputPath}", line);
    }

    [Fact]
    public void BuildLogLine_IncludesFailedItemExitCode()
    {
        var item = CreateItem("bad.dat", "bad.mp4");
        item.Status = QueueItemStatus.Failed;
        item.StatusText = "Failed";
        item.ConversionResult = CreateResult(
            item,
            isSuccess: false,
            processingTime: TimeSpan.FromSeconds(1.84),
            exitCode: 17);

        var line = QueueItemResultFormatter.BuildLogLine(item, 2, 3);

        Assert.Equal($"Queue item result: 2 of 3; bad.dat; Failed after 1.8 seconds; Output: {item.PlannedOutputPath}; Exit code: 17", line);
    }

    [Fact]
    public void BuildLogLine_UsesUnsupportedSkippedWordingWithoutFakeTime()
    {
        var item = CreateItem("MaterialFolderIndex.dat", "MaterialFolderIndex.mp4");
        item.Status = QueueItemStatus.Unsupported;
        item.StatusText = "Unsupported";
        item.ProgressText = "Will not process";
        item.ResultStatusSummary = "Skipped - unsupported video payload";

        var line = QueueItemResultFormatter.BuildLogLine(item, 2, 3);

        Assert.Equal("Queue item result: 2 of 3; MaterialFolderIndex.dat; Skipped - unsupported video payload; Output: not created", line);
    }

    [Fact]
    public void BuildLogLine_UsesExistingOutputSkippedWording()
    {
        var item = CreateItem("existing.dat", "existing.mp4");
        item.Status = QueueItemStatus.Skipped;
        item.StatusText = "Exists";
        item.ProgressText = "Selected output exists";
        item.HasExistingDirectOutput = true;

        var line = QueueItemResultFormatter.BuildLogLine(item, 3, 3);

        Assert.Equal($"Queue item result: 3 of 3; existing.dat; Skipped - output already exists; Output: {item.PlannedOutputPath}", line);
    }

    [Fact]
    public void BuildSummaryLine_UsesSameItemStatus()
    {
        var item = CreateItem("sample.dat", "sample.mp4");
        item.Status = QueueItemStatus.Completed;
        item.StatusText = "Completed";
        item.ConversionResult = CreateResult(
            item,
            isSuccess: true,
            processingTime: TimeSpan.FromSeconds(0.64),
            exitCode: 0);

        var line = QueueItemResultFormatter.BuildSummaryLine(item, 3, 3);

        Assert.Equal("3 of 3 - Completed in 0.6 seconds - sample.dat", line);
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

    private static ConversionResult CreateResult(
        QueueItem item,
        bool isSuccess,
        TimeSpan processingTime,
        int? exitCode)
    {
        return new ConversionResult(
            isSuccess,
            isSuccess ? "Completed." : "Failed.",
            "ffmpeg.exe",
            Array.Empty<string>(),
            item.InputPath,
            item.PlannedOutputPath,
            item.Fps,
            exitCode,
            "",
            "",
            ConversionMode: item.ConversionMode,
            OutputFormat: item.OutputFormat.DisplayName(),
            ProcessingTime: processingTime);
    }
}
