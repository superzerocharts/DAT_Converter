using System.Buffers.Binary;
using System.Globalization;
using System.Xml.Linq;

namespace DatConverter;

public sealed class MirasysFpsDetector
{
    public const double DefaultTimebaseUnitsPerSecond = 39062.5;

    private const int ReadBufferSize = 1024 * 1024;
    private const int CarryBytes = 23;
    private const int MinimumUsefulRecordCount = 2;
    private const uint MaximumPayloadSize = 100_000_000;

    public MirasysFpsDetectionResult Detect(string datPath, string? sefPath = null)
    {
        if (string.IsNullOrWhiteSpace(datPath))
        {
            return CreateFailure("A .dat path is required.");
        }

        if (!File.Exists(datPath))
        {
            return CreateFailure($"The .dat file was not found: {datPath}");
        }

        List<FrameRecord> records;
        long fileLength;
        try
        {
            fileLength = new FileInfo(datPath).Length;
            records = ScanFrameRecords(datPath, fileLength);
        }
        catch (IOException ex)
        {
            return CreateFailure($"Could not read the .dat file: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            return CreateFailure($"Could not access the .dat file: {ex.Message}");
        }

        if (records.Count < MinimumUsefulRecordCount)
        {
            return CreateFailure("Fewer than two valid Mirasys H264/I264 frame records were found.", records);
        }

        records.Sort(static (left, right) => left.Offset.CompareTo(right.Offset));

        var firstTimestamp = records[0].Timestamp;
        var lastTimestamp = records[^1].Timestamp;
        if (lastTimestamp <= firstTimestamp)
        {
            return CreateFailure("Unable to calculate FPS from Mirasys timestamps.", records);
        }

        var warnings = new List<string>();
        var timestampSpan = lastTimestamp - firstTimestamp;
        var sidecar = TryReadSidecarDuration(sefPath);
        double timebase;
        double durationSeconds;
        string detectionSource;

        if (sidecar.DurationSeconds.HasValue)
        {
            durationSeconds = sidecar.DurationSeconds.Value;
            timebase = timestampSpan / durationSeconds;
            detectionSource = "DatFrameRecordsWithSefDuration";
        }
        else
        {
            timebase = DefaultTimebaseUnitsPerSecond;
            durationSeconds = timestampSpan / DefaultTimebaseUnitsPerSecond;
            detectionSource = "DatFrameRecordsDefaultTimebase";

            if (string.IsNullOrWhiteSpace(sefPath))
            {
                warnings.Add("No .sef/.sef2 sidecar was supplied; using default Mirasys timestamp timebase.");
            }
            else
            {
                warnings.Add(sidecar.Warning ?? "The .sef/.sef2 sidecar was invalid; using default Mirasys timestamp timebase.");
            }
        }

        var details = BuildTechnicalDetails(records, firstTimestamp, lastTimestamp, timestampSpan, timebase, durationSeconds, warnings);
        return new MirasysFpsDetectionResult
        {
            Succeeded = true,
            DetectionSource = detectionSource,
            Confidence = GetDetectorConfidence(detectionSource, details),
            TechnicalDetails = details
        };
    }

    private static List<FrameRecord> ScanFrameRecords(string datPath, long fileLength)
    {
        var records = new List<FrameRecord>();
        using var stream = new FileStream(datPath, FileMode.Open, FileAccess.Read, FileShare.Read, ReadBufferSize, FileOptions.SequentialScan);
        var buffer = new byte[ReadBufferSize + CarryBytes];
        var carryLength = 0;
        long bytesReadFromFile = 0;

        while (true)
        {
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

            for (var index = 16; index < processLimit; index++)
            {
                if (!TryGetMarkerKind(buffer, index, out var markerKind))
                {
                    continue;
                }

                var offset = windowStartOffset + index;
                if (TryReadFrameRecord(buffer, index, offset, fileLength, markerKind, out var record))
                {
                    records.Add(record);
                }
            }

            carryLength = Math.Min(CarryBytes, total);
            Buffer.BlockCopy(buffer, total - carryLength, buffer, 0, carryLength);
        }

        return records;
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
        out FrameRecord record)
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

        record = new FrameRecord(markerOffset, markerKind, timestamp, (int)width, (int)height, payloadSize);
        return true;
    }

    private static SidecarDurationResult TryReadSidecarDuration(string? sefPath)
    {
        if (string.IsNullOrWhiteSpace(sefPath))
        {
            return new SidecarDurationResult(null, null);
        }

        if (!File.Exists(sefPath))
        {
            return new SidecarDurationResult(null, $"The .sef/.sef2 sidecar was not found: {sefPath}");
        }

        try
        {
            var document = XDocument.Load(sefPath);
            var startText = document.Descendants().FirstOrDefault(element => element.Name.LocalName == "start")?.Value;
            var endText = document.Descendants().FirstOrDefault(element => element.Name.LocalName == "end")?.Value;

            if (!DateTime.TryParse(startText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var start) ||
                !DateTime.TryParse(endText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var end))
            {
                return new SidecarDurationResult(null, "The .sef/.sef2 sidecar did not contain valid start/end timestamps.");
            }

            var duration = end - start;
            return duration > TimeSpan.Zero
                ? new SidecarDurationResult(duration.TotalSeconds, null)
                : new SidecarDurationResult(null, "The .sef/.sef2 sidecar duration was not positive.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return new SidecarDurationResult(null, $"The .sef/.sef2 sidecar could not be read: {ex.Message}");
        }
    }

    private static MirasysFpsTechnicalDetails BuildTechnicalDetails(
        IReadOnlyList<FrameRecord> records,
        ulong firstTimestamp,
        ulong lastTimestamp,
        ulong timestampSpan,
        double timebase,
        double durationSeconds,
        List<string> warnings)
    {
        var positiveDeltas = new List<ulong>();
        for (var index = 1; index < records.Count; index++)
        {
            if (records[index].Timestamp > records[index - 1].Timestamp)
            {
                positiveDeltas.Add(records[index].Timestamp - records[index - 1].Timestamp);
            }
        }

        var instantFps = positiveDeltas
            .Where(delta => delta > 0)
            .Select(delta => timebase / delta)
            .ToList();
        var bucketCounts = BuildBucketCounts(records, firstTimestamp, timebase);
        var stableBucketCounts = GetStableBucketCounts(bucketCounts);
        var first = records[0];
        var multipleResolutions = records.Any(record => record.Width != first.Width || record.Height != first.Height);

        AddEvidenceWarnings(records, positiveDeltas, instantFps, bucketCounts, stableBucketCounts, multipleResolutions, warnings);

        return new MirasysFpsTechnicalDetails
        {
            FrameCount = records.Count,
            H264KeyframeCount = records.Count(record => record.MarkerKind == "H264"),
            I264InterframeCount = records.Count(record => record.MarkerKind == "I264"),
            FirstTimestamp = firstTimestamp,
            LastTimestamp = lastTimestamp,
            StreamTimestampSpan = timestampSpan,
            Width = first.Width,
            Height = first.Height,
            MultipleResolutionsDetected = multipleResolutions,
            TimebaseUnitsPerSecond = timebase,
            DurationSeconds = durationSeconds,
            AverageFps = durationSeconds > 0 ? records.Count / durationSeconds : null,
            InstantFpsMedian = Median(instantFps),
            InstantFpsMin = instantFps.Count > 0 ? instantFps.Min() : null,
            InstantFpsMax = instantFps.Count > 0 ? instantFps.Max() : null,
            BucketMedianFps = Median(stableBucketCounts.Select(static value => (double)value).ToList()),
            BucketModeFps = Mode(stableBucketCounts),
            BucketMinFps = stableBucketCounts.Count > 0 ? stableBucketCounts.Min() : null,
            BucketMaxFps = stableBucketCounts.Count > 0 ? stableBucketCounts.Max() : null,
            BucketCount = bucketCounts.Count,
            PositiveTimestampDeltas = positiveDeltas,
            PerSecondBucketCounts = bucketCounts,
            StableBucketCounts = stableBucketCounts,
            Warnings = warnings
        };
    }

    private static List<int> BuildBucketCounts(IReadOnlyList<FrameRecord> records, ulong firstTimestamp, double timebase)
    {
        var buckets = new SortedDictionary<int, int>();
        foreach (var record in records)
        {
            var elapsedSeconds = (record.Timestamp - firstTimestamp) / timebase;
            var bucket = Math.Max(0, (int)Math.Floor(elapsedSeconds));
            buckets[bucket] = buckets.TryGetValue(bucket, out var count) ? count + 1 : 1;
        }

        return buckets.Values.ToList();
    }

    private static List<int> GetStableBucketCounts(IReadOnlyList<int> bucketCounts)
    {
        if (bucketCounts.Count <= 2)
        {
            return bucketCounts.ToList();
        }

        var counts = bucketCounts.ToList();
        var interiorMedian = Median(counts.Skip(1).Take(counts.Count - 2).Select(static value => (double)value).ToList());
        if (interiorMedian.HasValue)
        {
            if (counts[0] < interiorMedian.Value * 0.75)
            {
                counts.RemoveAt(0);
            }

            if (counts.Count > 1 && counts[^1] < interiorMedian.Value * 0.75)
            {
                counts.RemoveAt(counts.Count - 1);
            }
        }

        return counts;
    }

    private static void AddEvidenceWarnings(
        IReadOnlyList<FrameRecord> records,
        IReadOnlyList<ulong> positiveDeltas,
        IReadOnlyList<double> instantFps,
        IReadOnlyList<int> bucketCounts,
        IReadOnlyList<int> stableBucketCounts,
        bool multipleResolutions,
        List<string> warnings)
    {
        if (records.Count < 60)
        {
            warnings.Add("Low frame record count; FPS evidence may be weak.");
        }

        if (multipleResolutions)
        {
            warnings.Add("Multiple frame resolutions were detected.");
        }

        if (bucketCounts.Count > stableBucketCounts.Count)
        {
            warnings.Add("Partial first/last per-second bucket ignored for nominal FPS evidence.");
        }

        if (positiveDeltas.Count < records.Count - 1)
        {
            warnings.Add("One or more non-increasing timestamp deltas were ignored.");
        }

        if (instantFps.Count > 0)
        {
            var median = Median(instantFps.ToList());
            if (median.HasValue && (instantFps.Min() < median.Value * 0.5 || instantFps.Max() > median.Value * 1.5))
            {
                warnings.Add("Timestamp gap outliers were detected.");
            }
        }
    }

    private static string GetDetectorConfidence(string detectionSource, MirasysFpsTechnicalDetails details)
    {
        if (details.FrameCount < 60 || details.BucketCount < 3 || details.MultipleResolutionsDetected)
        {
            return "Low";
        }

        return string.Equals(detectionSource, "DatFrameRecordsWithSefDuration", StringComparison.Ordinal)
            ? "High"
            : "Medium";
    }

    private static MirasysFpsDetectionResult CreateFailure(string reason, IReadOnlyList<FrameRecord>? records = null)
    {
        return new MirasysFpsDetectionResult
        {
            Succeeded = false,
            FailureReason = reason,
            Confidence = "Low",
            TechnicalDetails = new MirasysFpsTechnicalDetails
            {
                FrameCount = records?.Count ?? 0,
                H264KeyframeCount = records?.Count(record => record.MarkerKind == "H264") ?? 0,
                I264InterframeCount = records?.Count(record => record.MarkerKind == "I264") ?? 0,
                Warnings = string.IsNullOrWhiteSpace(reason) ? Array.Empty<string>() : new[] { reason }
            }
        };
    }

    private static double? Median(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
        {
            return null;
        }

        var sorted = values.OrderBy(static value => value).ToArray();
        var middle = sorted.Length / 2;
        return sorted.Length % 2 == 1
            ? sorted[middle]
            : (sorted[middle - 1] + sorted[middle]) / 2;
    }

    private static int? Mode(IReadOnlyList<int> values)
    {
        return values.Count == 0
            ? null
            : values.GroupBy(static value => value)
                .OrderByDescending(static group => group.Count())
                .ThenBy(static group => group.Key)
                .First()
                .Key;
    }

    private readonly record struct FrameRecord(long Offset, string MarkerKind, ulong Timestamp, int Width, int Height, uint PayloadSize);

    private readonly record struct SidecarDurationResult(double? DurationSeconds, string? Warning);
}
