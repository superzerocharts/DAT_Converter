namespace DatConverter;

public sealed class FolderScanResult
{
    public FolderScanResult(
        IReadOnlyList<string> datFiles,
        bool stoppedBecauseTooManyFiles,
        IReadOnlyList<string> skippedPaths,
        IReadOnlyList<string> errors)
    {
        DatFiles = datFiles;
        StoppedBecauseTooManyFiles = stoppedBecauseTooManyFiles;
        SkippedPaths = skippedPaths;
        Errors = errors;
    }

    public IReadOnlyList<string> DatFiles { get; }

    public bool StoppedBecauseTooManyFiles { get; }

    public IReadOnlyList<string> SkippedPaths { get; }

    public IReadOnlyList<string> Errors { get; }
}
