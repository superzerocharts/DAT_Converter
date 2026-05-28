using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace DatConverter;

public sealed class DatPreviewWindowExtractor
{
    private const int ReadBufferSize = 1024 * 1024;
    private const int CarryBytes = 23;
    private const int HeaderBytesBeforeMarker = 16;
    private const int PayloadSizeBytesAfterMarker = 4;
    private const uint MaximumPayloadSize = 100_000_000;
    private static readonly TimeSpan PreviewWindowDuration = TimeSpan.FromSeconds(4);

    private readonly SpotterDatPayloadScanner scanner = new();

    public DatPreviewWindowResult ExtractWindow(
        string datPath,
        TimeSpan requestedLocalOffset,
        TimeSpan? segmentDuration,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        var endOffset = requestedLocalOffset + PreviewWindowDuration;
        return ExtractRange(datPath, requestedLocalOffset, endOffset, segmentDuration, outputPath, cancellationToken);
    }

    public DatPreviewWindowResult ExtractRange(
        string datPath,
        TimeSpan startLocalOffset,
        TimeSpan endLocalOffset,
        TimeSpan? segmentDuration,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(datPath))
        {
            return CreateFailure(datPath, startLocalOffset, outputPath, $"The .dat file was not found: {datPath}");
        }

        if (endLocalOffset <= startLocalOffset)
        {
            return CreateFailure(datPath, startLocalOffset, outputPath, "Trim end must be after trim start.");
        }

        var fileLength = new FileInfo(datPath).Length;
        List<DatPreviewFrameRecord> records;
        try
        {
            records = ScanFrameRecords(datPath, fileLength, segmentDuration, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return CreateFailure(datPath, startLocalOffset, outputPath, $"Preview scan could not read the .dat file: {ex.Message}");
        }

        if (records.Count == 0)
        {
            return CreateFailure(datPath, startLocalOffset, outputPath, "No H264/I264 frame records were found.");
        }

        var targetOffset = ClampOffset(startLocalOffset, segmentDuration);
        var targetEndOffset = ClampOffset(endLocalOffset, segmentDuration);
        var keyframe = ChooseKeyframe(records, targetOffset);
        if (keyframe is null)
        {
            return CreateFailure(datPath, startLocalOffset, outputPath, "No H264 keyframe record was found.");
        }

        var windowRecords = BuildWindowRecords(records, keyframe.LocalOffset, targetEndOffset);
        if (windowRecords.Count == 0)
        {
            return CreateFailure(datPath, startLocalOffset, outputPath, "No frame records were available for the preview window.");
        }

        try
        {
            var outputDirectory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            long writtenBytes = 0;
            var writtenRecords = 0;
            var payloadSizeBytes = new byte[4];
            using var input = new FileStream(datPath, FileMode.Open, FileAccess.Read, FileShare.Read, ReadBufferSize, FileOptions.RandomAccess);
            using var output = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, ReadBufferSize, FileOptions.SequentialScan);
            foreach (var record in windowRecords)
            {
                cancellationToken.ThrowIfCancellationRequested();
                input.Position = record.MarkerOffset + 4;
                if (input.Read(payloadSizeBytes) != payloadSizeBytes.Length)
                {
                    continue;
                }

                var payloadSize = BinaryPrimitives.ReadUInt32LittleEndian(payloadSizeBytes.AsSpan());
                if (payloadSize == 0 || payloadSize > MaximumPayloadSize || record.MarkerOffset + 8 + payloadSize > fileLength)
                {
                    continue;
                }

                var payload = new byte[payloadSize];
                var totalRead = 0;
                while (totalRead < payload.Length)
                {
                    var read = input.Read(payload, totalRead, payload.Length - totalRead);
                    if (read == 0)
                    {
                        break;
                    }

                    totalRead += read;
                }

                if (totalRead != payload.Length)
                {
                    continue;
                }

                var scan = scanner.Scan(payload);
                if (!scan.HasAnnexBStartCode)
                {
                    continue;
                }

                var payloadStart = scan.FirstStartCodeOffset!.Value;
                output.Write(payload, payloadStart, payload.Length - payloadStart);
                writtenRecords++;
                writtenBytes += payload.Length - payloadStart;
            }

            if (writtenRecords == 0)
            {
                TryDeleteFile(outputPath);
                return CreateFailure(datPath, startLocalOffset, outputPath, "Trim window did not contain usable Annex B H.264 payload bytes.");
            }

            var previewSeekOffset = targetOffset > keyframe.LocalOffset ? targetOffset - keyframe.LocalOffset : TimeSpan.Zero;
            return new DatPreviewWindowResult
            {
                Succeeded = true,
                OutputPath = outputPath,
                SourcePath = datPath,
                RequestedLocalOffset = startLocalOffset,
                SelectedKeyframeLocalOffset = keyframe.LocalOffset,
                PreviewSeekOffset = previewSeekOffset,
                ScannedFrameRecordCount = records.Count,
                WrittenFrameRecordCount = writtenRecords,
                WrittenPayloadBytes = writtenBytes,
                TechnicalDetails = BuildTechnicalDetails(datPath, startLocalOffset, records.Count, keyframe, writtenRecords, writtenBytes, null)
            };
        }
        catch (OperationCanceledException)
        {
            TryDeleteFile(outputPath);
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            TryDeleteFile(outputPath);
            return CreateFailure(datPath, startLocalOffset, outputPath, $"Trim window extraction failed: {ex.Message}", records.Count, keyframe);
        }
    }

    private static List<DatPreviewFrameRecord> BuildWindowRecords(
        IReadOnlyList<DatPreviewFrameRecord> records,
        TimeSpan keyframeOffset,
        TimeSpan targetEndOffset)
    {
        var result = new List<DatPreviewFrameRecord>();
        var includedAtOrAfterEnd = false;
        foreach (var record in records.Where(record => record.LocalOffset >= keyframeOffset))
        {
            result.Add(record);
            if (record.LocalOffset >= targetEndOffset)
            {
                includedAtOrAfterEnd = true;
                break;
            }
        }

        if (!includedAtOrAfterEnd && result.Count == 0)
        {
            return records
                .Where(record => record.LocalOffset >= keyframeOffset)
                .Take(1)
                .ToList();
        }

        return result;
    }

    internal IReadOnlyList<DatPreviewFrameRecord> ScanFrameRecordsForTests(string datPath, TimeSpan? segmentDuration)
    {
        return ScanFrameRecords(datPath, new FileInfo(datPath).Length, segmentDuration, CancellationToken.None);
    }

    private static DatPreviewFrameRecord? ChooseKeyframe(IReadOnlyList<DatPreviewFrameRecord> records, TimeSpan targetOffset)
    {
        return records
            .Where(record => string.Equals(record.MarkerKind, "H264", StringComparison.Ordinal) && record.LocalOffset <= targetOffset)
            .LastOrDefault()
            ?? records.FirstOrDefault(record => string.Equals(record.MarkerKind, "H264", StringComparison.Ordinal));
    }

    private static TimeSpan ClampOffset(TimeSpan requestedOffset, TimeSpan? segmentDuration)
    {
        if (requestedOffset < TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        return segmentDuration.HasValue && requestedOffset > segmentDuration.Value
            ? segmentDuration.Value
            : requestedOffset;
    }

    private static List<DatPreviewFrameRecord> ScanFrameRecords(
        string datPath,
        long fileLength,
        TimeSpan? segmentDuration,
        CancellationToken cancellationToken)
    {
        var rawRecords = new List<RawFrameRecord>();
        using var stream = new FileStream(datPath, FileMode.Open, FileAccess.Read, FileShare.Read, ReadBufferSize, FileOptions.SequentialScan);
        var buffer = new byte[ReadBufferSize + CarryBytes];
        var carryLength = 0;
        long bytesReadFromFile = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = stream.Read(buffer, carryLength, ReadBufferSize);
            if (read == 0)
            {
                break;
            }

            var total = carryLength + read;
            var windowStartOffset = bytesReadFromFile - carryLength;
            bytesReadFromFile += read;
            var isFinalWindow = bytesReadFromFile == fileLength;
            var processLimit = isFinalWindow ? total - 8 : Math.Max(0, total - CarryBytes);

            for (var index = HeaderBytesBeforeMarker; index < processLimit; index++)
            {
                if (!TryGetMarkerKind(buffer, index, out var markerKind))
                {
                    continue;
                }

                var markerOffset = windowStartOffset + index;
                if (TryReadFrameRecord(buffer, index, markerOffset, fileLength, markerKind, out var record))
                {
                    rawRecords.Add(record);
                }
            }

            carryLength = Math.Min(CarryBytes, total);
            Buffer.BlockCopy(buffer, total - carryLength, buffer, 0, carryLength);
        }

        if (rawRecords.Count == 0)
        {
            return [];
        }

        rawRecords.Sort(static (left, right) => left.MarkerOffset.CompareTo(right.MarkerOffset));
        var firstTimestamp = rawRecords[0].Timestamp;
        var lastTimestamp = rawRecords[^1].Timestamp;
        var timestampSpan = lastTimestamp > firstTimestamp ? lastTimestamp - firstTimestamp : 0;
        var duration = segmentDuration.HasValue && segmentDuration.Value > TimeSpan.Zero
            ? segmentDuration.Value
            : timestampSpan > 0
                ? TimeSpan.FromSeconds(timestampSpan / SpotterFpsDetector.DefaultTimebaseUnitsPerSecond)
                : TimeSpan.Zero;

        return rawRecords
            .Select(record => new DatPreviewFrameRecord(
                record.MarkerOffset,
                record.MarkerKind,
                record.Timestamp,
                record.PayloadSize,
                CalculateLocalOffset(record.Timestamp, firstTimestamp, timestampSpan, duration)))
            .ToList();
    }

    private static TimeSpan CalculateLocalOffset(ulong timestamp, ulong firstTimestamp, ulong timestampSpan, TimeSpan duration)
    {
        if (timestamp <= firstTimestamp || timestampSpan == 0 || duration <= TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        var relativeTicks = timestamp - firstTimestamp;
        var ratio = Math.Clamp(relativeTicks / (double)timestampSpan, 0, 1);
        return TimeSpan.FromSeconds(duration.TotalSeconds * ratio);
    }

    private static bool TryGetMarkerKind(byte[] buffer, int index, out string markerKind)
    {
        if (buffer[index + 1] == (byte)'2' &&
            buffer[index + 2] == (byte)'6' &&
            buffer[index + 3] == (byte)'4' &&
            (buffer[index] == (byte)'H' || buffer[index] == (byte)'I'))
        {
            markerKind = buffer[index] == (byte)'H' ? "H264" : "I264";
            return true;
        }

        markerKind = "";
        return false;
    }

    private static bool TryReadFrameRecord(
        byte[] buffer,
        int markerIndex,
        long markerOffset,
        long fileLength,
        string markerKind,
        out RawFrameRecord record)
    {
        var timestamp = BinaryPrimitives.ReadUInt64LittleEndian(buffer.AsSpan(markerIndex - 16, 8));
        var width = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(markerIndex - 8, 4));
        var height = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(markerIndex - 4, 4));
        var payloadSize = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(markerIndex + 4, 4));

        var isSane =
            width is > 0 and < 10000 &&
            height is > 0 and < 10000 &&
            payloadSize is > 0 and < MaximumPayloadSize &&
            markerOffset + 8 + payloadSize <= fileLength;

        if (!isSane)
        {
            record = default;
            return false;
        }

        record = new RawFrameRecord(markerOffset, markerKind, timestamp, payloadSize);
        return true;
    }

    private static DatPreviewWindowResult CreateFailure(
        string datPath,
        TimeSpan requestedLocalOffset,
        string outputPath,
        string reason,
        int scannedFrameRecordCount = 0,
        DatPreviewFrameRecord? selectedKeyframe = null)
    {
        TryDeleteFile(outputPath);
        return new DatPreviewWindowResult
        {
            Succeeded = false,
            SourcePath = datPath,
            RequestedLocalOffset = requestedLocalOffset,
            SelectedKeyframeLocalOffset = selectedKeyframe?.LocalOffset,
            ScannedFrameRecordCount = scannedFrameRecordCount,
            FailureReason = reason,
            TechnicalDetails = BuildTechnicalDetails(datPath, requestedLocalOffset, scannedFrameRecordCount, selectedKeyframe, 0, 0, reason)
        };
    }

    private static string BuildTechnicalDetails(
        string datPath,
        TimeSpan requestedLocalOffset,
        int scannedRecordCount,
        DatPreviewFrameRecord? selectedKeyframe,
        int writtenRecordCount,
        long writtenBytes,
        string? failureReason)
    {
        var builder = new StringBuilder();
        builder.AppendLine(CultureInfo.InvariantCulture, $"Source DAT: {datPath}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Requested local offset: {FormatOffset(requestedLocalOffset)}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Scanned frame records: {scannedRecordCount}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Selected keyframe local offset: {(selectedKeyframe is null ? "none" : FormatOffset(selectedKeyframe.LocalOffset))}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Selected keyframe marker offset: {selectedKeyframe?.MarkerOffset.ToString(CultureInfo.InvariantCulture) ?? "none"}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Written preview records: {writtenRecordCount}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Written preview bytes: {writtenBytes}");
        if (!string.IsNullOrWhiteSpace(failureReason))
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"Failure: {failureReason}");
        }

        return builder.ToString();
    }

    private static string FormatOffset(TimeSpan offset)
    {
        return offset.ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private readonly record struct RawFrameRecord(long MarkerOffset, string MarkerKind, ulong Timestamp, uint PayloadSize);
}
