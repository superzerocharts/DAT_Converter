namespace DatConverter;

public sealed class SpotterDatPayloadExtractionResult
{
    public bool Succeeded { get; init; }

    public string InputPath { get; init; } = "";

    public string? OutputPath { get; init; }

    public string? FailureReason { get; init; }

    public long InputFileSize { get; init; }

    public long ExtractedPayloadByteCount { get; init; }

    public double ExtractedSourcePercentage => InputFileSize > 0
        ? ExtractedPayloadByteCount * 100.0 / InputFileSize
        : 0;

    public long SkippedLeadingByteCount { get; init; }

    public long SkippedNonPayloadByteCount { get; init; }

    public int FrameRecordCount { get; init; }

    public int ExtractedFrameRecordCount { get; init; }

    public int CandidateNalUnitCount { get; init; }

    public int StartCodeCount => CandidateNalUnitCount;

    public int SpsCount { get; init; }

    public int PpsCount { get; init; }

    public int IdrFrameCount { get; init; }

    public IReadOnlyList<int> FirstNalTypes { get; init; } = Array.Empty<int>();

    public bool SkippedWrapperBytes { get; init; }

    public long SkippedWrapperByteCount { get; init; }

    public bool LookedConfident { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public string BuildTechnicalReport()
    {
        var lines = new List<string>
        {
            $"Spotter H.264 payload extraction succeeded: {Succeeded}",
            $"Input: {InputPath}",
            $"Output: {OutputPath ?? "not created"}",
            $"Input file size: {InputFileSize}",
            $"Source byte size: {InputFileSize}",
            $"Frame records found: {FrameRecordCount}",
            $"Frame records extracted: {ExtractedFrameRecordCount}",
            $"Extracted payload bytes: {ExtractedPayloadByteCount}",
            $"Extracted byte size: {ExtractedPayloadByteCount}",
            $"Extracted/source percentage: {ExtractedSourcePercentage:0.###}%",
            $"Skipped leading bytes: {SkippedLeadingByteCount}",
            $"Skipped non-payload bytes: {SkippedNonPayloadByteCount}",
            $"Start code count: {StartCodeCount}",
            $"Candidate H.264 NAL units: {CandidateNalUnitCount}",
            $"SPS count: {SpsCount}",
            $"PPS count: {PpsCount}",
            $"IDR frame count: {IdrFrameCount}",
            $"First NAL types: {(FirstNalTypes.Count == 0 ? "none" : string.Join(", ", FirstNalTypes))}",
            $"Wrapper/header bytes skipped: {SkippedWrapperBytes}",
            $"Wrapper/header byte count skipped: {SkippedWrapperByteCount}",
            $"Extraction looked confident: {LookedConfident}"
        };

        if (!Succeeded)
        {
            lines.Add($"Failure reason: {FailureReason ?? "unknown"}");
        }

        foreach (var warning in Warnings)
        {
            lines.Add($"Warning: {warning}");
        }

        return string.Join(Environment.NewLine, lines);
    }
}
