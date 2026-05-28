using System.Buffers.Binary;

namespace DatConverter;

public sealed class SpotterDatPayloadExtractor
{
    private const int RollingHeaderBytes = 20;
    private const int CopyBufferSize = 1024 * 1024;
    private const uint MaximumPayloadSize = 100_000_000;

    private readonly SpotterDatPayloadScanner scanner;

    public SpotterDatPayloadExtractor()
        : this(new SpotterDatPayloadScanner())
    {
    }

    public SpotterDatPayloadExtractor(SpotterDatPayloadScanner scanner)
    {
        this.scanner = scanner;
    }

    public SpotterDatPayloadExtractionResult Extract(string datPath, string outputPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(datPath))
        {
            return CreateFailure(datPath, outputPath, 0, "A .dat path is required.");
        }

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return CreateFailure(datPath, outputPath, 0, "An output .h264 path is required.");
        }

        if (!File.Exists(datPath))
        {
            return CreateFailure(datPath, outputPath, 0, $"The .dat file was not found: {datPath}");
        }

        var inputFileSize = new FileInfo(datPath).Length;
        var warnings = new List<string>();
        var firstNalTypes = new List<int>();
        var frameRecordCount = 0;
        var extractedFrameRecordCount = 0;
        long extractedPayloadBytes = 0;
        long? firstFrameRecordOffset = null;
        long skippedWrapperBytes = 0;
        long skippedNonPayloadBytes = 0;
        var candidateNalUnits = 0;
        var spsCount = 0;
        var ppsCount = 0;
        var idrFrameCount = 0;

        try
        {
            var outputDirectory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            using var input = new FileStream(datPath, FileMode.Open, FileAccess.Read, FileShare.Read, CopyBufferSize, FileOptions.SequentialScan);
            using var output = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, CopyBufferSize, FileOptions.SequentialScan);
            var rollingHeader = new byte[RollingHeaderBytes];
            var rollingCount = 0;

            while (input.Position < inputFileSize)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var currentByte = input.ReadByte();
                if (currentByte < 0)
                {
                    break;
                }

                AddRollingByte(rollingHeader, ref rollingCount, (byte)currentByte);
                if (rollingCount < RollingHeaderBytes || !EndsWithSpotterMarker(rollingHeader))
                {
                    continue;
                }

                var markerOffset = input.Position - 4;
                var payloadSizeBytes = new byte[4];
                var payloadSizeRead = input.Read(payloadSizeBytes, 0, payloadSizeBytes.Length);
                if (payloadSizeRead != payloadSizeBytes.Length)
                {
                    break;
                }

                var payloadSize = BinaryPrimitives.ReadUInt32LittleEndian(payloadSizeBytes);
                if (!IsSaneRecord(rollingHeader, markerOffset, payloadSize, inputFileSize))
                {
                    input.Position = markerOffset + 1;
                    rollingCount = 0;
                    continue;
                }

                frameRecordCount++;
                firstFrameRecordOffset ??= markerOffset - 16;
                skippedNonPayloadBytes += 24;
                var payload = new byte[payloadSize];
                var totalRead = 0;
                while (totalRead < payload.Length)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var read = input.Read(payload, totalRead, payload.Length - totalRead);
                    if (read == 0)
                    {
                        warnings.Add("A frame payload ended before the expected payload size.");
                        break;
                    }

                    totalRead += read;
                }

                if (totalRead != payload.Length)
                {
                    break;
                }

                var scan = scanner.Scan(payload);
                if (!scan.HasAnnexBStartCode)
                {
                    warnings.Add($"Skipped frame record {frameRecordCount} because its payload did not contain an Annex B start code.");
                    rollingCount = 0;
                    continue;
                }

                var payloadStart = scan.FirstStartCodeOffset!.Value;
                if (payloadStart > 0)
                {
                    skippedWrapperBytes += payloadStart;
                    skippedNonPayloadBytes += payloadStart;
                }

                output.Write(payload, payloadStart, payload.Length - payloadStart);
                extractedFrameRecordCount++;
                extractedPayloadBytes += payload.Length - payloadStart;
                candidateNalUnits += scan.CandidateNalUnitCount;
                spsCount += scan.SpsCount;
                ppsCount += scan.PpsCount;
                idrFrameCount += scan.IdrFrameCount;
                foreach (var nalType in scan.FirstNalTypes)
                {
                    if (firstNalTypes.Count >= 12)
                    {
                        break;
                    }

                    firstNalTypes.Add(nalType);
                }

                rollingCount = 0;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            TryDeleteEmptyOrPartialOutput(outputPath);
            return CreateFailure(datPath, outputPath, inputFileSize, $"Payload extraction could not complete: {ex.Message}");
        }
        catch (OperationCanceledException)
        {
            TryDeleteEmptyOrPartialOutput(outputPath);
            throw;
        }

        if (extractedFrameRecordCount == 0)
        {
            TryDeleteEmptyOrPartialOutput(outputPath);
            var failureWarnings = BuildConfidenceWarnings(
                succeeded: false,
                frameRecordCount,
                extractedFrameRecordCount,
                candidateNalUnits,
                spsCount,
                ppsCount,
                idrFrameCount,
                warnings);
            return new SpotterDatPayloadExtractionResult
            {
                Succeeded = false,
                InputPath = datPath,
                OutputPath = null,
                FailureReason = "No confident Spotter H264/I264 frame payloads with Annex B start codes were found.",
                InputFileSize = inputFileSize,
                FrameRecordCount = frameRecordCount,
                SkippedLeadingByteCount = Math.Max(0, firstFrameRecordOffset ?? 0),
                SkippedNonPayloadByteCount = inputFileSize,
                LookedConfident = false,
                Warnings = failureWarnings
            };
        }

        var confidenceWarnings = BuildConfidenceWarnings(
            succeeded: true,
            frameRecordCount,
            extractedFrameRecordCount,
            candidateNalUnits,
            spsCount,
            ppsCount,
            idrFrameCount,
            warnings);
        var lookedConfident = confidenceWarnings.Count == 0;
        skippedNonPayloadBytes += Math.Max(0, firstFrameRecordOffset ?? 0);
        skippedNonPayloadBytes = Math.Max(skippedNonPayloadBytes, inputFileSize - extractedPayloadBytes);

        return new SpotterDatPayloadExtractionResult
        {
            Succeeded = true,
            InputPath = datPath,
            OutputPath = outputPath,
            InputFileSize = inputFileSize,
            ExtractedPayloadByteCount = extractedPayloadBytes,
            SkippedLeadingByteCount = Math.Max(0, firstFrameRecordOffset ?? 0),
            SkippedNonPayloadByteCount = skippedNonPayloadBytes,
            FrameRecordCount = frameRecordCount,
            ExtractedFrameRecordCount = extractedFrameRecordCount,
            CandidateNalUnitCount = candidateNalUnits,
            SpsCount = spsCount,
            PpsCount = ppsCount,
            IdrFrameCount = idrFrameCount,
            FirstNalTypes = firstNalTypes,
            SkippedWrapperBytes = skippedWrapperBytes > 0,
            SkippedWrapperByteCount = skippedWrapperBytes,
            LookedConfident = lookedConfident,
            Warnings = confidenceWarnings.Take(20).ToList()
        };
    }

    private static bool EndsWithSpotterMarker(byte[] rollingHeader)
    {
        return rollingHeader[17] == (byte)'2' &&
               rollingHeader[18] == (byte)'6' &&
               rollingHeader[19] == (byte)'4' &&
               (rollingHeader[16] == (byte)'H' || rollingHeader[16] == (byte)'I');
    }

    private static bool IsSaneRecord(byte[] rollingHeader, long markerOffset, uint payloadSize, long fileLength)
    {
        var width = BinaryPrimitives.ReadUInt32LittleEndian(rollingHeader.AsSpan(8, 4));
        var height = BinaryPrimitives.ReadUInt32LittleEndian(rollingHeader.AsSpan(12, 4));

        return width is > 0 and < 10000 &&
               height is > 0 and < 10000 &&
               payloadSize is > 0 and < MaximumPayloadSize &&
               markerOffset + 8 + payloadSize <= fileLength;
    }

    private static void AddRollingByte(byte[] rollingHeader, ref int rollingCount, byte value)
    {
        if (rollingCount < rollingHeader.Length)
        {
            rollingHeader[rollingCount++] = value;
            return;
        }

        Buffer.BlockCopy(rollingHeader, 1, rollingHeader, 0, rollingHeader.Length - 1);
        rollingHeader[^1] = value;
    }

    private static SpotterDatPayloadExtractionResult CreateFailure(string? datPath, string? outputPath, long inputFileSize, string reason)
    {
        return new SpotterDatPayloadExtractionResult
        {
            Succeeded = false,
            InputPath = datPath ?? "",
            OutputPath = outputPath,
            InputFileSize = inputFileSize,
            SkippedNonPayloadByteCount = inputFileSize,
            FailureReason = reason,
            LookedConfident = false,
            Warnings = new[] { "Extraction did not produce a clean H.264 output and must not be used automatically." }
        };
    }

    private static List<string> BuildConfidenceWarnings(
        bool succeeded,
        int frameRecordCount,
        int extractedFrameRecordCount,
        int candidateNalUnits,
        int spsCount,
        int ppsCount,
        int idrFrameCount,
        IReadOnlyList<string> warnings)
    {
        var result = warnings.ToList();
        if (!succeeded)
        {
            result.Add("Extraction did not produce a clean H.264 output and must not be used automatically.");
        }

        if (frameRecordCount == 0)
        {
            result.Add("No frame records were identified.");
        }

        if (extractedFrameRecordCount < frameRecordCount)
        {
            result.Add("One or more frame records were not extracted.");
        }

        if (candidateNalUnits == 0)
        {
            result.Add("No H.264 Annex B start codes were identified.");
        }

        if (spsCount == 0)
        {
            result.Add("No SPS NAL units were identified.");
        }

        if (ppsCount == 0)
        {
            result.Add("No PPS NAL units were identified.");
        }

        if (idrFrameCount == 0)
        {
            result.Add("No IDR NAL units were identified.");
        }

        return result
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static void TryDeleteEmptyOrPartialOutput(string outputPath)
    {
        try
        {
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }
        }
        catch
        {
        }
    }
}
