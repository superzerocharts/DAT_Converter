namespace DatConverter.Tests;

public sealed class SpotterMultiFileExportDetectorTests
{
    [Fact]
    public void Detect_Sef2ListsMultipleDvrFiles_ResolvesSelectedSegment()
    {
        using var temp = new TempDirectory();
        var files = CreateSegmentFiles(temp.Path, 4);
        WriteSidecar(
            Path.Combine(temp.Path, "export.sef2"),
            "dvrfile00000001.dat",
            "dvrfile00000002.dat",
            "dvrfile00000003.dat",
            "dvrfile00000004.dat");

        var result = new SpotterMultiFileExportDetector().Detect(files[1]);

        Assert.NotNull(result.Context);
        Assert.Equal(2, result.Context.SegmentNumber);
        Assert.Equal(4, result.Context.SegmentCount);
        Assert.Equal("Multi-file export detected: segment 2 of 4.", result.Context.DisplayText);
        Assert.Contains("dvrfile00000001.dat", result.TechnicalLogText);
        Assert.Contains("dvrfile00000004.dat", result.TechnicalLogText);
    }

    [Fact]
    public void Detect_StandaloneDatWithoutSidecars_ReportsNoContext()
    {
        using var temp = new TempDirectory();
        var datPath = Path.Combine(temp.Path, "sample.dat");
        File.WriteAllText(datPath, "not relevant");

        var result = new SpotterMultiFileExportDetector().Detect(datPath);

        Assert.Null(result.Context);
        Assert.Null(result.TechnicalLogText);
    }

    [Fact]
    public void Detect_MissingSidecars_DoNotFailItemCreationScenario()
    {
        using var temp = new TempDirectory();
        var item = CreateItem(Path.Combine(temp.Path, "dvrfile00000001.dat"));
        File.WriteAllText(item.InputPath, "not relevant");

        var result = new SpotterMultiFileExportDetector().Detect(item.InputPath);
        item.MultiFileExportContext = result.Context;

        Assert.Null(item.MultiFileExportContext);
        Assert.Null(result.TechnicalLogText);
    }

    [Fact]
    public void Detect_MalformedSidecar_DoesNotFailAndLogsTechnicalDetails()
    {
        using var temp = new TempDirectory();
        var datPath = Path.Combine(temp.Path, "dvrfile00000001.dat");
        File.WriteAllText(datPath, "not relevant");
        File.WriteAllText(Path.Combine(temp.Path, "bad.sef2"), "<archive2><files>");

        var result = new SpotterMultiFileExportDetector().Detect(datPath);

        Assert.Null(result.Context);
        Assert.Contains("sidecar could not be read", result.TechnicalLogText);
    }

    [Fact]
    public void Detect_WithRealFourHourExport_WhenPresent_ResolvesSegmentOneAndTwo()
    {
        var sampleDirectory = @"W:\Projects\Cam 8379 - 4 hr clip";
        var first = Path.Combine(sampleDirectory, "dvrfile00000001.dat");
        var second = Path.Combine(sampleDirectory, "dvrfile00000002.dat");
        if (!File.Exists(first) || !File.Exists(second))
        {
            return;
        }

        var detector = new SpotterMultiFileExportDetector();
        var firstResult = detector.Detect(first);
        var secondResult = detector.Detect(second);

        Assert.NotNull(firstResult.Context);
        Assert.NotNull(secondResult.Context);
        Assert.Equal(1, firstResult.Context.SegmentNumber);
        Assert.Equal(2, secondResult.Context.SegmentNumber);
        Assert.Equal(4, firstResult.Context.SegmentCount);
        Assert.Equal(4, secondResult.Context.SegmentCount);
    }

    private static List<string> CreateSegmentFiles(string folder, int count)
    {
        var paths = new List<string>();
        for (var index = 1; index <= count; index++)
        {
            var path = Path.Combine(folder, $"dvrfile{index:00000000}.dat");
            File.WriteAllText(path, "not relevant");
            paths.Add(path);
        }

        return paths;
    }

    private static QueueItem CreateItem(string inputPath)
    {
        return new QueueItem(
            inputPath,
            Path.ChangeExtension(inputPath, ".mp4"),
            OutputDestinationMode.SameFolderAsSource,
            null,
            OutputFormat.Mp4,
            "Fast",
            FpsOption.FromLabel("30"),
            hasExistingDirectOutput: false);
    }

    private static void WriteSidecar(string path, params string[] fileNames)
    {
        var fileLines = string.Join(
            Environment.NewLine,
            fileNames.Select(fileName => $"""    <file name="{fileName}" />"""));
        File.WriteAllText(
            path,
            $"""
            <archive2>
              <files>
            {fileLines}
              </files>
            </archive2>
            """);
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
