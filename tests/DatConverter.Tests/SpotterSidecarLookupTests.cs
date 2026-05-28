namespace DatConverter.Tests;

public sealed class SpotterSidecarLookupTests
{
    [Fact]
    public void FindSidecarForDat_SingleReferencedDat_ReturnsSidecar()
    {
        using var temp = new TempDirectory();
        var datPath = Path.Combine(temp.Path, "clip.dat");
        var sidecarPath = Path.Combine(temp.Path, "clip.sef2");
        File.WriteAllText(datPath, "not relevant");
        WriteSidecar(sidecarPath, "clip.dat");

        var result = SpotterSidecarLookup.FindSidecarForDat(datPath);

        Assert.Equal(sidecarPath, result);
    }

    [Fact]
    public void FindSidecarForDat_MultiSegmentSidecarReferencesSelectedDat_ReturnsNull()
    {
        using var temp = new TempDirectory();
        var datPath = Path.Combine(temp.Path, "dvrfile00000002.dat");
        File.WriteAllText(datPath, "not relevant");
        WriteSidecar(
            Path.Combine(temp.Path, "export.sef2"),
            "dvrfile00000001.dat",
            "dvrfile00000002.dat",
            "dvrfile00000003.dat");

        var result = SpotterSidecarLookup.FindSidecarForDat(datPath);

        Assert.Null(result);
    }

    [Fact]
    public void FindSidecarForDat_SidecarWithoutFileReferences_PreservesFallback()
    {
        using var temp = new TempDirectory();
        var datPath = Path.Combine(temp.Path, "clip.dat");
        var sidecarPath = Path.Combine(temp.Path, "clip.sef2");
        File.WriteAllText(datPath, "not relevant");
        File.WriteAllText(
            sidecarPath,
            """
            <archive2>
              <start>2026-05-20T04:25:16.5410000</start>
              <end>2026-05-20T04:30:14.7380000</end>
            </archive2>
            """);

        var result = SpotterSidecarLookup.FindSidecarForDat(datPath);

        Assert.Equal(sidecarPath, result);
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
