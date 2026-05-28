using System.Xml.Linq;

namespace DatConverter;

public sealed class SpotterMultiFileExportDetector
{
    public SpotterMultiFileExportDetectionResult Detect(string datPath)
    {
        if (string.IsNullOrWhiteSpace(datPath))
        {
            return new SpotterMultiFileExportDetectionResult();
        }

        var folder = Path.GetDirectoryName(datPath);
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            return new SpotterMultiFileExportDetectionResult();
        }

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
            return new SpotterMultiFileExportDetectionResult
            {
                TechnicalLogText = $"Multi-file export detection could not scan sidecars near {datPath}: {ex.Message}"
            };
        }

        if (sidecarPaths.Count == 0)
        {
            return new SpotterMultiFileExportDetectionResult();
        }

        var selectedFileName = Path.GetFileName(datPath);
        var warnings = new List<string>();
        foreach (var sidecarPath in sidecarPaths)
        {
            var sidecar = TryReadSidecarSegmentFiles(sidecarPath);
            if (sidecar.Warning is not null)
            {
                warnings.Add(sidecar.Warning);
                continue;
            }

            var segmentFileNames = sidecar.SegmentFileNames;
            if (segmentFileNames.Count <= 1)
            {
                continue;
            }

            var index = IndexOf(segmentFileNames, selectedFileName);
            if (index < 0)
            {
                continue;
            }

            var context = new SpotterMultiFileExportContext
            {
                SidecarPath = sidecarPath,
                SegmentNumber = index + 1,
                SegmentCount = segmentFileNames.Count,
                SegmentFileNames = segmentFileNames
            };

            return new SpotterMultiFileExportDetectionResult
            {
                Context = context,
                TechnicalLogText = context.BuildTechnicalLogText()
            };
        }

        return warnings.Count == 0
            ? new SpotterMultiFileExportDetectionResult()
            : new SpotterMultiFileExportDetectionResult
            {
                TechnicalLogText = string.Join(Environment.NewLine, warnings)
            };
    }

    private static SidecarSegmentFileResult TryReadSidecarSegmentFiles(string sidecarPath)
    {
        try
        {
            var document = XDocument.Load(sidecarPath);
            var segmentFileNames = document
                .Descendants()
                .Where(element => string.Equals(element.Name.LocalName, "file", StringComparison.OrdinalIgnoreCase))
                .Select(element => (string?)element.Attribute("name"))
                .Select(GetDatFileName)
                .Where(fileName => !string.IsNullOrWhiteSpace(fileName))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Cast<string>()
                .ToList();

            return new SidecarSegmentFileResult(segmentFileNames, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return new SidecarSegmentFileResult(
                Array.Empty<string>(),
                $"Multi-file export sidecar could not be read: {sidecarPath}; Error: {ex.Message}");
        }
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

    private static int IndexOf(IReadOnlyList<string> values, string value)
    {
        for (var index = 0; index < values.Count; index++)
        {
            if (string.Equals(values[index], value, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    private readonly record struct SidecarSegmentFileResult(IReadOnlyList<string> SegmentFileNames, string? Warning);
}
