using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Xml.Linq;

namespace DatConverter;

public sealed class SpotterStoryboardExportDetector
{
    private const int IndexRecordBytes = 44;
    private static readonly IndexLayout[] IndexLayouts =
    {
        new(HeaderBytes: 21, TrailerBytes: 16, SegmentNumberOffset: 11, StartTicksOffset: 15, EndTicksOffset: 23),
        new(HeaderBytes: 24, TrailerBytes: 13, SegmentNumberOffset: 7, StartTicksOffset: 11, EndTicksOffset: 19)
    };

    public SpotterStoryboardPlan Detect(string path)
    {
        var folder = ResolveFolder(path);
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            return new SpotterStoryboardPlan
            {
                ExportFolder = folder ?? "",
                Warnings = new[] { "The export folder was not found." }
            };
        }

        var warnings = new List<string>();
        IReadOnlyList<string> sidecarPaths;
        try
        {
            sidecarPaths = Directory.EnumerateFiles(folder, "*.sef")
                .Concat(Directory.EnumerateFiles(folder, "*.sef2"))
                .OrderBy(sidecarPath => sidecarPath, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new SpotterStoryboardPlan
            {
                ExportFolder = folder,
                Warnings = new[] { $"Could not scan metadata files: {ex.Message}" }
            };
        }

        foreach (var sidecarPath in sidecarPaths)
        {
            var plan = TryReadStoryboardSidecar(folder, sidecarPath);
            if (plan.IsStoryboardExport)
            {
                return plan;
            }

            if (plan.Warnings.Count > 0)
            {
                warnings.AddRange(plan.Warnings);
            }
        }

        return new SpotterStoryboardPlan
        {
            ExportFolder = folder,
            Warnings = warnings
        };
    }

    private static SpotterStoryboardPlan TryReadStoryboardSidecar(string folder, string sidecarPath)
    {
        var warnings = new List<string>();
        XDocument document;
        try
        {
            document = XDocument.Load(sidecarPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return new SpotterStoryboardPlan
            {
                ExportFolder = folder,
                SidecarPath = sidecarPath,
                Warnings = new[] { $"Storyboard metadata file could not be read: {sidecarPath}; Error: {ex.Message}" }
            };
        }

        var storyboardData = document
            .Descendants()
            .FirstOrDefault(element => string.Equals(element.Name.LocalName, "StoryboardData", StringComparison.OrdinalIgnoreCase));
        if (storyboardData is null)
        {
            return new SpotterStoryboardPlan { ExportFolder = folder, SidecarPath = sidecarPath };
        }

        var storyboard = storyboardData
            .Descendants()
            .FirstOrDefault(element => string.Equals(element.Name.LocalName, "storyboard", StringComparison.OrdinalIgnoreCase));
        var storyboardName = (string?)storyboard?.Attribute("name");
        var clipElements = storyboardData
            .Descendants()
            .Where(element => string.Equals(element.Name.LocalName, "clip", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var fileNames = ReadDatFileNames(document);
        var channels = ReadChannels(document);
        var nodeToChannel = ReadLayoutNodeChannelMap(document);
        var materialRecords = ReadMaterialFolderIndex(folder, warnings);
        var evidence = new List<string>
        {
            $"StoryboardData found in {sidecarPath}.",
            $"Storyboard clips: {clipElements.Count}.",
            $"DAT files listed: {fileNames.Count}.",
            $"Video channels listed: {channels.Ordered.Count}."
        };

        if (materialRecords.Count > 0)
        {
            evidence.Add($"Material folder index records: {materialRecords.Count}.");
        }

        if (clipElements.Count == 0)
        {
            warnings.Add("StoryboardData did not contain any clips.");
        }

        if (clipElements.Count != fileNames.Count)
        {
            warnings.Add("Storyboard clip count and DAT file count do not match.");
        }

        var clips = new List<SpotterStoryboardClip>();
        for (var index = 0; index < clipElements.Count && index < fileNames.Count; index++)
        {
            var clipElement = clipElements[index];
            var nodePath = clipElement
                .Descendants()
                .Where(element => string.Equals(element.Name.LocalName, "node", StringComparison.OrdinalIgnoreCase))
                .Select(element => ((string?)element.Attribute("path"))?.Trim())
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";
            var timeElement = clipElement
                .Descendants()
                .FirstOrDefault(element => string.Equals(element.Name.LocalName, "time", StringComparison.OrdinalIgnoreCase));
            var layoutChannelId = nodeToChannel.TryGetValue(nodePath, out var resolvedChannelId)
                ? resolvedChannelId
                : (int?)null;
            var channel = layoutChannelId.HasValue && channels.TryGetValue(layoutChannelId.Value, out var matchedChannel)
                ? matchedChannel
                : index < channels.Ordered.Count ? channels.Ordered[index] : null;
            var datFileName = fileNames[index];
            materialRecords.TryGetValue(index + 1, out var materialRecord);

            if (string.IsNullOrWhiteSpace(nodePath))
            {
                warnings.Add($"Storyboard clip {index + 1} does not have a node path.");
            }

            if (!layoutChannelId.HasValue && !string.IsNullOrWhiteSpace(nodePath))
            {
                warnings.Add($"Storyboard clip {index + 1} node path could not be mapped through LayoutData.");
            }

            var datFilePath = Path.Combine(folder, datFileName);
            if (!File.Exists(datFilePath))
            {
                warnings.Add($"Storyboard clip {index + 1} references missing DAT file: {datFileName}.");
            }

            clips.Add(new SpotterStoryboardClip
            {
                ClipNumber = index + 1,
                ClipName = ((string?)clipElement.Attribute("name"))?.Trim() ?? $"Clip {index + 1}",
                ClipStartTime = ParseStoryboardTime((string?)timeElement?.Attribute("start")),
                ClipEndTime = ParseStoryboardTime((string?)timeElement?.Attribute("end")),
                NodePath = nodePath,
                ChannelId = layoutChannelId ?? channel?.ChannelId,
                CameraDisplayName = channel?.CameraDisplayName,
                DatFileName = datFileName,
                DatFilePath = datFilePath,
                MaterialStartTime = materialRecord?.StartTime,
                MaterialEndTime = materialRecord?.EndTime
            });
        }

        return new SpotterStoryboardPlan
        {
            ExportFolder = folder,
            SidecarPath = sidecarPath,
            StoryboardName = string.IsNullOrWhiteSpace(storyboardName) ? "Storyboard" : storyboardName.Trim(),
            Clips = clips,
            Warnings = warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Evidence = evidence
        };
    }

    private static string? ResolveFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var fullPath = Path.GetFullPath(path);
        return Directory.Exists(fullPath)
            ? fullPath
            : Path.GetDirectoryName(fullPath);
    }

    private static IReadOnlyList<string> ReadDatFileNames(XDocument document)
    {
        return document
            .Descendants()
            .Where(element => string.Equals(element.Name.LocalName, "files", StringComparison.OrdinalIgnoreCase))
            .Elements()
            .Where(element => string.Equals(element.Name.LocalName, "file", StringComparison.OrdinalIgnoreCase))
            .Select(element => GetDatFileName((string?)element.Attribute("name")))
            .Where(fileName => !string.IsNullOrWhiteSpace(fileName))
            .Cast<string>()
            .ToList();
    }

    private static ChannelReadResult ReadChannels(XDocument document)
    {
        var ordered = document
            .Descendants()
            .Where(element => string.Equals(element.Name.LocalName, "channels", StringComparison.OrdinalIgnoreCase))
            .Elements()
            .Where(element => string.Equals(element.Name.LocalName, "channel", StringComparison.OrdinalIgnoreCase))
            .Where(element => HasAttributeValue(element, "dataType", "Video") && HasAttributeValue(element, "channelType", "Material"))
            .Select(element => new StoryboardChannel(
                ChannelId: int.TryParse((string?)element.Attribute("channelId"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var channelId) ? channelId : null,
                CameraDisplayName: DecodeCameraDisplayName((string?)element.Attribute("name"))))
            .Where(channel => channel.ChannelId.HasValue)
            .ToList();

        return new ChannelReadResult(
            ordered,
            ordered
                .Where(channel => channel.ChannelId.HasValue)
                .GroupBy(channel => channel.ChannelId!.Value)
                .ToDictionary(group => group.Key, group => group.First(), EqualityComparer<int>.Default));
    }

    private static Dictionary<string, int> ReadLayoutNodeChannelMap(XDocument document)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in document
            .Descendants()
            .Where(element => string.Equals(element.Name.LocalName, "LayoutData", StringComparison.OrdinalIgnoreCase))
            .Descendants()
            .Where(element => string.Equals(element.Name.LocalName, "node", StringComparison.OrdinalIgnoreCase)))
        {
            var id = ((string?)node.Attribute("id"))?.Trim();
            var data = ((string?)node.Attribute("data"))?.Trim();
            if (string.IsNullOrWhiteSpace(id) ||
                string.IsNullOrWhiteSpace(data) ||
                !id.StartsWith("Camera", StringComparison.OrdinalIgnoreCase) ||
                !int.TryParse(id["Camera".Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var channelId))
            {
                continue;
            }

            try
            {
                var video = XElement.Parse(data);
                var profileNodeId = ((string?)video.Attribute("ProfileNodeId"))?.Trim();
                if (!string.IsNullOrWhiteSpace(profileNodeId))
                {
                    result[profileNodeId] = channelId;
                }
            }
            catch (System.Xml.XmlException)
            {
            }
        }

        return result;
    }

    private static IReadOnlyDictionary<int, MaterialIndexRecord> ReadMaterialFolderIndex(string folder, List<string> warnings)
    {
        var indexPath = Path.Combine(folder, "MaterialFolderIndex.dat");
        if (!File.Exists(indexPath))
        {
            return new Dictionary<int, MaterialIndexRecord>();
        }

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(indexPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            warnings.Add($"Material folder index could not be read: {ex.Message}");
            return new Dictionary<int, MaterialIndexRecord>();
        }

        foreach (var layout in IndexLayouts)
        {
            var parsed = TryReadMaterialFolderIndex(bytes, layout);
            if (parsed is not null)
            {
                return parsed.ToDictionary(record => record.SegmentNumber);
            }
        }

        warnings.Add("Material folder index does not match the expected record layout.");
        return new Dictionary<int, MaterialIndexRecord>();
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

    private static DateTime? ParseStoryboardTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed.UtcDateTime
            : null;
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

    private static bool HasAttributeValue(XElement element, string attributeName, string expectedValue)
    {
        return string.Equals((string?)element.Attribute(attributeName), expectedValue, StringComparison.OrdinalIgnoreCase);
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

    private sealed record ChannelReadResult(
        IReadOnlyList<StoryboardChannel> Ordered,
        IReadOnlyDictionary<int, StoryboardChannel> ByChannelId)
    {
        public bool TryGetValue(int channelId, out StoryboardChannel channel)
        {
            return ByChannelId.TryGetValue(channelId, out channel!);
        }
    }

    private sealed record StoryboardChannel(int? ChannelId, string? CameraDisplayName);

    private sealed record MaterialIndexRecord(int SegmentNumber, DateTime StartTime, DateTime EndTime);

    private readonly record struct IndexLayout(
        int HeaderBytes,
        int TrailerBytes,
        int SegmentNumberOffset,
        int StartTicksOffset,
        int EndTicksOffset);
}
