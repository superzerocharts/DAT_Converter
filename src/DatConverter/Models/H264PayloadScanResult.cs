namespace DatConverter;

public sealed class H264PayloadScanResult
{
    public int CandidateNalUnitCount { get; init; }

    public int SpsCount { get; init; }

    public int PpsCount { get; init; }

    public int IdrFrameCount { get; init; }

    public IReadOnlyList<int> FirstNalTypes { get; init; } = Array.Empty<int>();

    public int? FirstStartCodeOffset { get; init; }

    public bool HasAnnexBStartCode => FirstStartCodeOffset.HasValue;
}
