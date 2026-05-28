namespace DatConverter.Tests;

public sealed class SpotterDatPayloadScannerTests
{
    [Fact]
    public void Scan_DetectsAnnexBStartCodes()
    {
        var bytes = new byte[]
        {
            0x00, 0x00, 0x01, 0x67, 0x11,
            0x00, 0x00, 0x00, 0x01, 0x68, 0x22
        };

        var result = new SpotterDatPayloadScanner().Scan(bytes);

        Assert.True(result.HasAnnexBStartCode);
        Assert.Equal(2, result.CandidateNalUnitCount);
        Assert.Equal(0, result.FirstStartCodeOffset);
    }

    [Fact]
    public void Scan_IdentifiesSpsPpsAndIdrNalTypes()
    {
        var bytes = new byte[]
        {
            0x00, 0x00, 0x01, 0x67, 0x11,
            0x00, 0x00, 0x01, 0x68, 0x22,
            0x00, 0x00, 0x01, 0x65, 0x33,
            0x00, 0x00, 0x01, 0x41, 0x44
        };

        var result = new SpotterDatPayloadScanner().Scan(bytes);

        Assert.Equal(4, result.CandidateNalUnitCount);
        Assert.Equal(1, result.SpsCount);
        Assert.Equal(1, result.PpsCount);
        Assert.Equal(1, result.IdrFrameCount);
        Assert.Equal(new[] { 7, 8, 5, 1 }, result.FirstNalTypes);
    }

    [Fact]
    public void Scan_RandomDataReportsNoNalUnits()
    {
        var bytes = new byte[] { 0x12, 0x34, 0x56, 0x78, 0x90 };

        var result = new SpotterDatPayloadScanner().Scan(bytes);

        Assert.False(result.HasAnnexBStartCode);
        Assert.Equal(0, result.CandidateNalUnitCount);
        Assert.Empty(result.FirstNalTypes);
    }
}
