namespace DatConverter;

public sealed class SpotterMultiFileExportContext
{
    public string SidecarPath { get; init; } = "";

    public int SegmentNumber { get; init; }

    public int SegmentCount { get; init; }

    public IReadOnlyList<string> SegmentFileNames { get; init; } = Array.Empty<string>();

    public string DisplayText => $"Multi-file export detected: segment {SegmentNumber} of {SegmentCount}.";

    public string BuildTechnicalLogText()
    {
        var lines = new List<string>
        {
            DisplayText,
            $"Sidecar: {SidecarPath}",
            $"Segments: {string.Join(", ", SegmentFileNames)}"
        };

        return string.Join(Environment.NewLine, lines);
    }
}
