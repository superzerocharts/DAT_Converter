namespace DatConverter.Tests;

public sealed class ProbeServiceIntegrationTests
{
    [Fact]
    public async Task ProbeRawH264Async_AcceptsProvidedSampleDatWithBundledTools()
    {
        var samplePath = Path.Combine(GetRepositoryRoot(), "test-assets", "samples", "dat_5min_sample.dat");
        Assert.True(File.Exists(samplePath), $"Sample file is missing: {samplePath}");
        var originalLength = new FileInfo(samplePath).Length;
        var originalTimestamp = File.GetLastWriteTimeUtc(samplePath);
        var tools = ToolPathService.ResolveBundledTools();
        Assert.True(tools.AreAvailable, $"Bundled FFmpeg tools are missing from test output: {tools.FfmpegPath}; {tools.FfprobePath}");
        var service = new ProbeService(tools);

        var result = await service.ProbeRawH264Async(samplePath, FpsOption.FromLabel("30"), CancellationToken.None);

        Assert.True(result.IsSuccess, result.TechnicalDetails);
        Assert.Equal("h264", result.CodecName, ignoreCase: true);
        Assert.Equal("30", result.Fps.Label);
        Assert.Equal("30", result.Fps.FfmpegValue);
        Assert.Equal(originalLength, new FileInfo(samplePath).Length);
        Assert.Equal(originalTimestamp, File.GetLastWriteTimeUtc(samplePath));
    }

    [Fact]
    public async Task ProbeRawH264Async_RejectsInvalidDatWithUnsupportedMessage()
    {
        using var temp = new TempDirectory();
        var invalidDatPath = Path.Combine(temp.Path, "invalid.dat");
        File.WriteAllText(invalidDatPath, "not raw h264 video data");
        var tools = ToolPathService.ResolveBundledTools();
        Assert.True(tools.AreAvailable, $"Bundled FFmpeg tools are missing from test output: {tools.FfmpegPath}; {tools.FfprobePath}");
        var service = new ProbeService(tools);

        var result = await service.ProbeRawH264Async(invalidDatPath, FpsOption.FromLabel("30"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ProbeResult.UnsupportedMessage, result.UserMessage);
        Assert.Contains("-f h264", BuildProbeCommandForAssertion(invalidDatPath, result.Fps));
    }

    private static string GetRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DatConverter.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory.FullName;
    }

    private static string BuildProbeCommandForAssertion(string inputPath, FpsOption fps)
    {
        return $"-v error -f h264 -framerate {fps.FfmpegValue} -i \"{inputPath}\"";
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
