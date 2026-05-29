using System.Buffers.Binary;
using System.Text;
using System.Xml.Linq;

namespace DatConverter;

public sealed class SpotterSplitExportPlanBuilder
{
    private const int IndexRecordBytes = 44;
    private static readonly IndexLayout[] IndexLayouts =
    {
        new(HeaderBytes: 21, TrailerBytes: 16, SegmentNumberOffset: 11, StartTicksOffset: 15, EndTicksOffset: 23),
        new(HeaderBytes: 24, TrailerBytes: 13, SegmentNumberOffset: 7, StartTicksOffset: 11, EndTicksOffset: 19)
    };
    private static readonly TimeSpan ContinuityTolerance = TimeSpan.FromSeconds(2);

    public SpotterSplitExportPlan Build(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return CreateEmpty("", null, "A selected .dat path or export folder path is required.");
        }

        var selectedSourcePath = File.Exists(path) && string.Equals(Path.GetExtension(path), ".dat", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFullPath(path)
            : null;
        var folder = Directory.Exists(path)
            ? Path.GetFullPath(path)
            : Path.GetDirectoryName(Path.GetFullPath(path));
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            return CreateEmpty(folder ?? "", selectedSourcePath, "The export folder was not found.");
        }

        var warnings = new List<string>();
        var evidence = new List<string>();
        var sidecar = ReadBestSidecar(folder, warnings);
        var index = ReadMaterialFolderIndex(folder, warnings);

        if (sidecar.FileNames.Count <= 1)
        {
            if (sidecar.FileNames.Count == 1)
            {
                evidence.Add("Sidecar references one .dat file; no split export plan was built.");
            }

            return new SpotterSplitExportPlan
            {
                ExportFolder = folder,
                SelectedSourcePath = selectedSourcePath,
                LogicalOutputBaseName = ResolveLogicalOutputBaseName(sidecar.Path, folder),
                CameraDisplayName = sidecar.CameraDisplayName,
                Confidence = "None",
                Warnings = warnings,
                Evidence = evidence
            };
        }

        evidence.Add($"Sidecar lists {sidecar.FileNames.Count} .dat segment files.");
        if (!string.IsNullOrWhiteSpace(sidecar.Path))
        {
            evidence.Add($"Sidecar: {sidecar.Path}");
        }

        if (!string.IsNullOrWhiteSpace(sidecar.CameraDisplayName))
        {
            evidence.Add($"Sidecar camera display name: {sidecar.CameraDisplayName}");
        }

        var selectedSegmentNumber = ResolveSelectedSegmentNumber(selectedSourcePath, sidecar.FileNames);
        var missingFiles = sidecar.FileNames
            .Select(fileName => Path.Combine(folder, fileName))
            .Where(path => !File.Exists(path))
            .ToList();
        if (missingFiles.Count > 0)
        {
            warnings.Add("One or more referenced .dat segment files are missing.");
        }

        if (index.Records.Count > 0)
        {
            evidence.Add($"Material folder index contains {index.Records.Count} segment timing records.");
        }

        if (index.Records.Count > 0 && index.Records.Count != sidecar.FileNames.Count)
        {
            warnings.Add("Sidecar segment count and index segment count do not match.");
        }

        if (index.Records.Count > 0 && !HasSequentialSegmentNumbers(index.Records))
        {
            warnings.Add("Index segment numbers are not a clear 1..N sequence.");
        }

        var orderedRecords = index.Records
            .OrderBy(record => record.SegmentNumber)
            .ToList();
        var timingBySegment = orderedRecords.ToDictionary(record => record.SegmentNumber);
        var segments = new List<SpotterSplitExportSegment>();
        for (var indexInSidecar = 0; indexInSidecar < sidecar.FileNames.Count; indexInSidecar++)
        {
            var segmentNumber = indexInSidecar + 1;
            timingBySegment.TryGetValue(segmentNumber, out var timing);
            var gap = CalculateGap(segmentNumber, timingBySegment);
            segments.Add(new SpotterSplitExportSegment
            {
                SegmentNumber = segmentNumber,
                FileName = sidecar.FileNames[indexInSidecar],
                FilePath = Path.Combine(folder, sidecar.FileNames[indexInSidecar]),
                StartTime = timing?.StartTime,
                EndTime = timing?.EndTime,
                GapFromPrevious = gap
            });
        }

        AddTimingWarnings(segments, warnings);
        var confidence = DetermineConfidence(segments, missingFiles, warnings);
        return new SpotterSplitExportPlan
        {
            ExportFolder = folder,
            SelectedSourcePath = selectedSourcePath,
            LogicalOutputBaseName = ResolveLogicalOutputBaseName(sidecar.Path, folder),
            CameraDisplayName = sidecar.CameraDisplayName,
            Segments = segments,
            SelectedSegmentNumber = selectedSegmentNumber,
            Confidence = confidence,
            Warnings = warnings,
            Evidence = evidence
        };
    }

    private static SpotterSplitExportPlan CreateEmpty(string folder, string? selectedSourcePath, string warning)
    {
        return new SpotterSplitExportPlan
        {
            ExportFolder = folder,
            SelectedSourcePath = selectedSourcePath,
            LogicalOutputBaseName = ResolveLogicalOutputBaseName(null, folder),
            Confidence = "None",
            Warnings = new[] { warning }
        };
    }

    private static string? ResolveLogicalOutputBaseName(string? sidecarPath, string folder)
    {
        var sidecarBaseName = Path.GetFileNameWithoutExtension(sidecarPath);
        if (!string.IsNullOrWhiteSpace(sidecarBaseName) &&
            sidecarBaseName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0)
        {
            return sidecarBaseName;
        }

        var folderName = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.IsNullOrWhiteSpace(folderName) || folderName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            ? null
            : folderName;
    }

    private static SidecarSegmentList ReadBestSidecar(string folder, List<string> warnings)
    {
        IReadOnlyList<string> sidecarPaths;
        try
        {
            sidecarPaths = Directory.EnumerateFiles(folder, "*.sef")
                .Concat(Directory.EnumerateFiles(folder, "*.sef2"))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            warnings.Add($"Could not scan sidecar files: {ex.Message}");
            return new SidecarSegmentList(null, Array.Empty<string>(), null);
        }

        SidecarSegmentList best = new(null, Array.Empty<string>(), null);
        foreach (var sidecarPath in sidecarPaths)
        {
            var parsed = ReadSidecar(sidecarPath, warnings);
            if (parsed.FileNames.Count > best.FileNames.Count)
            {
                best = parsed;
            }
        }

        return best;
    }

    private static SidecarSegmentList ReadSidecar(string sidecarPath, List<string> warnings)
    {
        try
        {
            var document = XDocument.Load(sidecarPath);
            var fileNames = document
                .Descendants()
                .Where(element => string.Equals(element.Name.LocalName, "file", StringComparison.OrdinalIgnoreCase))
                .Select(element => GetDatFileName((string?)element.Attribute("name")))
                .Where(fileName => !string.IsNullOrWhiteSpace(fileName))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Cast<string>()
                .ToList();
            var cameraDisplayName = ExtractCameraDisplayName(document);

            return new SidecarSegmentList(sidecarPath, fileNames, cameraDisplayName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            warnings.Add($"Sidecar could not be read: {sidecarPath}; Error: {ex.Message}");
            return new SidecarSegmentList(sidecarPath, Array.Empty<string>(), null);
        }
    }

    private static MaterialIndexRecords ReadMaterialFolderIndex(string folder, List<string> warnings)
    {
        var indexPath = Path.Combine(folder, "MaterialFolderIndex.dat");
        if (!File.Exists(indexPath))
        {
            return new MaterialIndexRecords(indexPath, Array.Empty<MaterialIndexRecord>());
        }

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(indexPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            warnings.Add($"Material folder index could not be read: {ex.Message}");
            return new MaterialIndexRecords(indexPath, Array.Empty<MaterialIndexRecord>());
        }

        foreach (var layout in IndexLayouts)
        {
            var parsed = TryReadMaterialFolderIndex(bytes, layout);
            if (parsed is not null)
            {
                return new MaterialIndexRecords(indexPath, parsed);
            }
        }

        warnings.Add("Material folder index does not match the expected record layout.");
        return new MaterialIndexRecords(indexPath, Array.Empty<MaterialIndexRecord>());
    }

    private static IReadOnlyList<MaterialIndexRecord>? TryReadMaterialFolderIndex(byte[] bytes, IndexLayout layout)
    {
        var payloadBytes = bytes.Length - layout.HeaderBytes - layout.TrailerBytes;
        if (payloadBytes <= 0 || payloadBytes % IndexRecordBytes != 0)
        {
            return null;
        }

        var records = new List<MaterialIndexRecord>();
        var recordCount = payloadBytes / IndexRecordBytes;
        for (var index = 0; index < recordCount; index++)
        {
            var offset = layout.HeaderBytes + index * IndexRecordBytes;
            var segmentNumber = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset + layout.SegmentNumberOffset, 4));
            var startTicks = BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(offset + layout.StartTicksOffset, 8));
            var endTicks = BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(offset + layout.EndTicksOffset, 8));
            if (segmentNumber <= 0 || !TryCreateDateTime(startTicks, out var start) || !TryCreateDateTime(endTicks, out var end))
            {
                return null;
            }

            records.Add(new MaterialIndexRecord(segmentNumber, start, end));
        }

        return records;
    }

    private static bool TryCreateDateTime(long ticks, out DateTime value)
    {
        if (ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks)
        {
            value = default;
            return false;
        }

        value = new DateTime(ticks, DateTimeKind.Unspecified);
        return true;
    }

    private static int? ResolveSelectedSegmentNumber(string? selectedSourcePath, IReadOnlyList<string> fileNames)
    {
        if (string.IsNullOrWhiteSpace(selectedSourcePath))
        {
            return null;
        }

        var selectedFileName = Path.GetFileName(selectedSourcePath);
        for (var index = 0; index < fileNames.Count; index++)
        {
            if (string.Equals(fileNames[index], selectedFileName, StringComparison.OrdinalIgnoreCase))
            {
                return index + 1;
            }
        }

        return null;
    }

    private static TimeSpan? CalculateGap(int segmentNumber, IReadOnlyDictionary<int, MaterialIndexRecord> timingBySegment)
    {
        if (segmentNumber <= 1 ||
            !timingBySegment.TryGetValue(segmentNumber, out var current) ||
            !timingBySegment.TryGetValue(segmentNumber - 1, out var previous))
        {
            return null;
        }

        return current.StartTime - previous.EndTime;
    }

    private static void AddTimingWarnings(IReadOnlyList<SpotterSplitExportSegment> segments, List<string> warnings)
    {
        foreach (var segment in segments)
        {
            if (segment.StartTime.HasValue && segment.EndTime.HasValue && segment.EndTime.Value <= segment.StartTime.Value)
            {
                warnings.Add($"Segment {segment.SegmentNumber} has non-positive timing duration.");
            }

            if (segment.GapFromPrevious.HasValue && segment.GapFromPrevious.Value.Duration() > ContinuityTolerance)
            {
                warnings.Add($"Segment {segment.SegmentNumber} timing is not continuous with the previous segment.");
            }
        }
    }

    private static string DetermineConfidence(
        IReadOnlyList<SpotterSplitExportSegment> segments,
        IReadOnlyList<string> missingFiles,
        IReadOnlyList<string> warnings)
    {
        if (segments.Count <= 1)
        {
            return "None";
        }

        return missingFiles.Count == 0 && warnings.Count == 0
            ? "Strong"
            : "Weak";
    }

    private static bool HasSequentialSegmentNumbers(IReadOnlyList<MaterialIndexRecord> records)
    {
        var ordered = records.Select(record => record.SegmentNumber).OrderBy(number => number).ToList();
        for (var index = 0; index < ordered.Count; index++)
        {
            if (ordered[index] != index + 1)
            {
                return false;
            }
        }

        return true;
    }

    private static string? GetDatFileName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var fileName = Path.GetFileName(value.Trim());
        return string.Equals(Path.GetExtension(fileName), ".dat", StringComparison.OrdinalIgnoreCase)
            ? fileName
            : null;
    }

    private static string? ExtractCameraDisplayName(XDocument document)
    {
        foreach (var element in document.Descendants())
        {
            if (!IsCameraNameCandidateElement(element.Name.LocalName))
            {
                continue;
            }

            var decoded = DecodeCameraDisplayName((string?)element.Attribute("name"));
            if (!string.IsNullOrWhiteSpace(decoded))
            {
                return decoded;
            }
        }

        return null;
    }

    private static bool IsCameraNameCandidateElement(string localName)
    {
        return string.Equals(localName, "video", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(localName, "material", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(localName, "videoChannel", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(localName, "materialChannel", StringComparison.OrdinalIgnoreCase);
    }

    private static string? DecodeCameraDisplayName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            var bytes = Convert.FromBase64String(value.Trim());
            var decoded = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetString(bytes)
                .Trim();
            return string.IsNullOrWhiteSpace(decoded) || decoded.Contains('\uFFFD')
                ? null
                : decoded;
        }
        catch (FormatException)
        {
            return null;
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }

    private readonly record struct SidecarSegmentList(string? Path, IReadOnlyList<string> FileNames, string? CameraDisplayName);

    private readonly record struct MaterialIndexRecords(string Path, IReadOnlyList<MaterialIndexRecord> Records);

    private sealed record MaterialIndexRecord(int SegmentNumber, DateTime StartTime, DateTime EndTime);

    private readonly record struct IndexLayout(
        int HeaderBytes,
        int TrailerBytes,
        int SegmentNumberOffset,
        int StartTicksOffset,
        int EndTicksOffset);
}
