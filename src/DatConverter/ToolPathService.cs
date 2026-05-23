namespace DatConverter;

public static class ToolPathService
{
    private const string ToolsFolderName = "tools";
    private const string FfmpegFolderName = "ffmpeg";
    private const string FfmpegExecutableName = "ffmpeg.exe";
    private const string FfprobeExecutableName = "ffprobe.exe";

    public static FfmpegTools ResolveBundledTools()
    {
        var applicationBaseDirectory = AppContext.BaseDirectory;
        var ffmpegPath = Path.Combine(applicationBaseDirectory, ToolsFolderName, FfmpegFolderName, FfmpegExecutableName);
        var ffprobePath = Path.Combine(applicationBaseDirectory, ToolsFolderName, FfmpegFolderName, FfprobeExecutableName);

        return new FfmpegTools(
            applicationBaseDirectory,
            ffmpegPath,
            ffprobePath,
            File.Exists(ffmpegPath),
            File.Exists(ffprobePath));
    }
}
