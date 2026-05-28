namespace DatConverter;

public sealed class SpotterDatPayloadScanner
{
    private const int MaxFirstNalTypes = 12;

    public H264PayloadScanResult Scan(ReadOnlySpan<byte> bytes)
    {
        var firstNalTypes = new List<int>();
        int? firstStartCodeOffset = null;
        var candidateNalUnits = 0;
        var spsCount = 0;
        var ppsCount = 0;
        var idrFrameCount = 0;

        var index = 0;
        while (TryFindStartCode(bytes, index, out var startCodeOffset, out var startCodeLength))
        {
            var nalHeaderOffset = startCodeOffset + startCodeLength;
            if (nalHeaderOffset >= bytes.Length)
            {
                break;
            }

            firstStartCodeOffset ??= startCodeOffset;
            var nalType = bytes[nalHeaderOffset] & 0x1F;
            candidateNalUnits++;
            if (firstNalTypes.Count < MaxFirstNalTypes)
            {
                firstNalTypes.Add(nalType);
            }

            switch (nalType)
            {
                case 5:
                    idrFrameCount++;
                    break;
                case 7:
                    spsCount++;
                    break;
                case 8:
                    ppsCount++;
                    break;
            }

            index = nalHeaderOffset + 1;
        }

        return new H264PayloadScanResult
        {
            CandidateNalUnitCount = candidateNalUnits,
            SpsCount = spsCount,
            PpsCount = ppsCount,
            IdrFrameCount = idrFrameCount,
            FirstNalTypes = firstNalTypes,
            FirstStartCodeOffset = firstStartCodeOffset
        };
    }

    public static bool TryFindStartCode(ReadOnlySpan<byte> bytes, int startIndex, out int offset, out int length)
    {
        for (var index = Math.Max(0, startIndex); index <= bytes.Length - 3; index++)
        {
            if (bytes[index] != 0 || bytes[index + 1] != 0)
            {
                continue;
            }

            if (bytes[index + 2] == 1)
            {
                offset = index;
                length = 3;
                return true;
            }

            if (index <= bytes.Length - 4 && bytes[index + 2] == 0 && bytes[index + 3] == 1)
            {
                offset = index;
                length = 4;
                return true;
            }
        }

        offset = -1;
        length = 0;
        return false;
    }
}
