namespace DatConverter.Tests;

public sealed class SpotterSplitExportPlanBuilderTests
{
    [Fact]
    public void Build_WithValidSidecarAndIndex_BuildsOrderedStrongPlan()
    {
        using var temp = new TempDirectory();
        var files = CreateSegmentFiles(temp.Path, 3);
        WriteSidecar(Path.Combine(temp.Path, "export.sef2"), files.Select(Path.GetFileName)!);
        var start = new DateTime(2026, 5, 22, 3, 59, 59, 480);
        WriteMaterialFolderIndex(
            temp.Path,
            (1, start, start.AddSeconds(10)),
            (2, start.AddSeconds(10).AddMilliseconds(34), start.AddSeconds(20)),
            (3, start.AddSeconds(20).AddMilliseconds(34), start.AddSeconds(30)));

        var plan = new SpotterSplitExportPlanBuilder().Build(temp.Path);

        Assert.True(plan.IsSplitExport);
        Assert.True(plan.IsStrongConfidence);
        Assert.Equal("Strong", plan.Confidence);
        Assert.Equal(3, plan.SegmentCount);
        Assert.Equal(files, plan.Segments.Select(segment => segment.FilePath).ToList());
        Assert.Equal("export", plan.LogicalOutputBaseName);
        Assert.Equal(34, plan.Segments[1].GapFromPrevious!.Value.TotalMilliseconds);
        Assert.Empty(plan.Warnings);
    }

    [Fact]
    public void Build_UsesSidecarFileNameAsLogicalOutputBaseName()
    {
        using var temp = new TempDirectory();
        var files = CreateSegmentFiles(temp.Path, 2);
        WriteSidecar(Path.Combine(temp.Path, "Cam 8379 - 4 hr clip.sef2"), files.Select(Path.GetFileName)!);
        var start = new DateTime(2026, 5, 22, 3, 59, 59, 480);
        WriteMaterialFolderIndex(
            temp.Path,
            (1, start, start.AddSeconds(10)),
            (2, start.AddSeconds(10).AddMilliseconds(34), start.AddSeconds(20)));

        var plan = new SpotterSplitExportPlanBuilder().Build(temp.Path);

        Assert.Equal("Cam 8379 - 4 hr clip", plan.LogicalOutputBaseName);
    }

    [Fact]
    public void Build_WithSidecarCameraName_DecodesCameraDisplayName()
    {
        using var temp = new TempDirectory();
        var files = CreateSegmentFiles(temp.Path, 2);
        WriteSidecar(
            Path.Combine(temp.Path, "Cam 8379 - 4 hr clip.sef2"),
            "ODM3OSBNYXJxdWVlIE5vcnRoZWFzdCBQVFo=",
            files.Select(Path.GetFileName)!);
        var start = new DateTime(2026, 5, 22, 3, 59, 59, 480);
        WriteMaterialFolderIndex(
            temp.Path,
            (1, start, start.AddSeconds(10)),
            (2, start.AddSeconds(10).AddMilliseconds(34), start.AddSeconds(20)));

        var plan = new SpotterSplitExportPlanBuilder().Build(temp.Path);

        Assert.Equal("8379 Marquee Northeast PTZ", plan.CameraDisplayName);
    }

    [Fact]
    public void Build_WithInvalidSidecarCameraName_IgnoresCameraDisplayName()
    {
        using var temp = new TempDirectory();
        var files = CreateSegmentFiles(temp.Path, 2);
        WriteSidecar(
            Path.Combine(temp.Path, "Cam 8379 - 4 hr clip.sef2"),
            "not valid base64",
            files.Select(Path.GetFileName)!);
        var start = new DateTime(2026, 5, 22, 3, 59, 59, 480);
        WriteMaterialFolderIndex(
            temp.Path,
            (1, start, start.AddSeconds(10)),
            (2, start.AddSeconds(10).AddMilliseconds(34), start.AddSeconds(20)));

        var plan = new SpotterSplitExportPlanBuilder().Build(temp.Path);

        Assert.Null(plan.CameraDisplayName);
        Assert.Equal("Cam 8379 - 4 hr clip", plan.LogicalOutputBaseName);
    }

    [Fact]
    public void Build_WithSelectedSegment_ResolvesSelectedSegmentNumber()
    {
        using var temp = new TempDirectory();
        var files = CreateSegmentFiles(temp.Path, 3);
        WriteSidecar(Path.Combine(temp.Path, "export.sef2"), files.Select(Path.GetFileName)!);
        var start = new DateTime(2026, 5, 22, 3, 59, 59, 480);
        WriteMaterialFolderIndex(
            temp.Path,
            (1, start, start.AddSeconds(10)),
            (2, start.AddSeconds(10).AddMilliseconds(34), start.AddSeconds(20)),
            (3, start.AddSeconds(20).AddMilliseconds(34), start.AddSeconds(30)));

        var plan = new SpotterSplitExportPlanBuilder().Build(files[1]);

        Assert.Equal(files[1], plan.SelectedSourcePath);
        Assert.Equal(2, plan.SelectedSegmentNumber);
        Assert.Equal("Strong", plan.Confidence);
    }

    [Fact]
    public void Build_StandaloneDat_ReturnsNoSplitExportPlan()
    {
        using var temp = new TempDirectory();
        var datPath = Path.Combine(temp.Path, "single.dat");
        File.WriteAllText(datPath, "not relevant");

        var plan = new SpotterSplitExportPlanBuilder().Build(datPath);

        Assert.False(plan.IsSplitExport);
        Assert.Equal("None", plan.Confidence);
        Assert.Empty(plan.Segments);
    }

    [Fact]
    public void Build_MissingSidecarAndIndex_FailsSafely()
    {
        using var temp = new TempDirectory();
        CreateSegmentFiles(temp.Path, 2);

        var plan = new SpotterSplitExportPlanBuilder().Build(temp.Path);

        Assert.False(plan.IsSplitExport);
        Assert.Equal("None", plan.Confidence);
        Assert.Empty(plan.Segments);
    }

    [Fact]
    public void Build_MalformedSidecar_FailsSafely()
    {
        using var temp = new TempDirectory();
        CreateSegmentFiles(temp.Path, 2);
        File.WriteAllText(Path.Combine(temp.Path, "bad.sef2"), "<archive2><files>");

        var plan = new SpotterSplitExportPlanBuilder().Build(temp.Path);

        Assert.False(plan.IsSplitExport);
        Assert.Equal("None", plan.Confidence);
        Assert.Contains(plan.Warnings, warning => warning.Contains("Sidecar could not be read", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Build_ReferencedMissingDatFiles_ProducesWeakConfidence()
    {
        using var temp = new TempDirectory();
        var first = Path.Combine(temp.Path, "dvrfile00000001.dat");
        File.WriteAllText(first, "not relevant");
        WriteSidecar(
            Path.Combine(temp.Path, "export.sef2"),
            "dvrfile00000001.dat",
            "dvrfile00000002.dat");

        var start = new DateTime(2026, 5, 22, 3, 59, 59, 480);
        WriteMaterialFolderIndex(
            temp.Path,
            (1, start, start.AddSeconds(10)),
            (2, start.AddSeconds(10).AddMilliseconds(34), start.AddSeconds(20)));

        var plan = new SpotterSplitExportPlanBuilder().Build(temp.Path);

        Assert.True(plan.IsSplitExport);
        Assert.Equal("Weak", plan.Confidence);
        Assert.Contains(plan.Warnings, warning => warning.Contains("missing", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Build_ContradictoryTimingMetadata_ProducesWeakConfidence()
    {
        using var temp = new TempDirectory();
        var files = CreateSegmentFiles(temp.Path, 2);
        WriteSidecar(Path.Combine(temp.Path, "export.sef2"), files.Select(Path.GetFileName)!);
        var start = new DateTime(2026, 5, 22, 3, 59, 59, 480);
        WriteMaterialFolderIndex(
            temp.Path,
            (1, start, start.AddSeconds(10)),
            (2, start.AddMinutes(10), start.AddMinutes(11)));

        var plan = new SpotterSplitExportPlanBuilder().Build(temp.Path);

        Assert.True(plan.IsSplitExport);
        Assert.Equal("Weak", plan.Confidence);
        Assert.Contains(plan.Warnings, warning => warning.Contains("not continuous", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Build_WithRealFourSegmentExport_WhenPresent_ReportsAllSegmentsInOrder()
    {
        var sampleDirectory = @"W:\Projects\Cam 8379 - 4 hr clip";
        if (!Directory.Exists(sampleDirectory))
        {
            return;
        }

        var selectedPath = Path.Combine(sampleDirectory, "dvrfile00000002.dat");
        var plan = new SpotterSplitExportPlanBuilder().Build(selectedPath);

        Assert.Equal("Strong", plan.Confidence);
        Assert.Equal(4, plan.SegmentCount);
        Assert.Equal(2, plan.SelectedSegmentNumber);
        Assert.Equal("8379 Marquee Northeast PTZ", plan.CameraDisplayName);
        Assert.Equal(
            new[]
            {
                "dvrfile00000001.dat",
                "dvrfile00000002.dat",
                "dvrfile00000003.dat",
                "dvrfile00000004.dat"
            },
            plan.Segments.Select(segment => segment.FileName).ToArray());
        Assert.All(plan.Segments.Skip(1), segment => Assert.True(segment.GapFromPrevious!.Value.TotalMilliseconds is >= 0 and <= 100));
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

    private static void WriteSidecar(string path, IEnumerable<string?> fileNames)
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

    private static void WriteSidecar(string path, string cameraDisplayNameBase64, IEnumerable<string?> fileNames)
    {
        var fileLines = string.Join(
            Environment.NewLine,
            fileNames.Select(fileName => $"""    <file name="{fileName}" />"""));
        File.WriteAllText(
            path,
            $"""
            <archive2>
              <channels>
                <channel dataType="Video" channelType="Material" name="{cameraDisplayNameBase64}" manufacturer="AXIS" model="AXIS Q6135-LE" />
              </channels>
              <files>
            {fileLines}
              </files>
            </archive2>
            """);
    }

    private static void WriteSidecar(string path, params string[] fileNames)
    {
        WriteSidecar(path, (IEnumerable<string?>)fileNames);
    }

    private static void WriteMaterialFolderIndex(string folder, params (int SegmentNumber, DateTime Start, DateTime End)[] records)
    {
        using var stream = new FileStream(Path.Combine(folder, "MaterialFolderIndex.dat"), FileMode.Create, FileAccess.Write);
        using var writer = new BinaryWriter(stream);
        writer.Write(new byte[24]);
        foreach (var record in records)
        {
            writer.Write(new byte[7]);
            writer.Write(record.SegmentNumber);
            writer.Write(record.Start.Ticks);
            writer.Write(record.End.Ticks);
            writer.Write(new byte[17]);
        }

        writer.Write(new byte[13]);
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
