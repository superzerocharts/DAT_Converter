namespace DatConverter;

public sealed record FfmpegTools(
    string ApplicationBaseDirectory,
    string FfmpegPath,
    string FfprobePath,
    bool FfmpegExists,
    bool FfprobeExists)
{
    public bool AreAvailable => FfmpegExists && FfprobeExists;
}
