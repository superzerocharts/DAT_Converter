using System.Xml.Linq;

namespace DatConverter;

public static class SpotterSidecarLookup
{
    public static string? FindSidecarForDat(string datPath)
    {
        var folder = Path.GetDirectoryName(datPath);
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            return null;
        }

        var datFileName = Path.GetFileName(datPath);
        var candidates = Directory.EnumerateFiles(folder, "*.sef")
            .Concat(Directory.EnumerateFiles(folder, "*.sef2"))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (candidates.Count == 0)
        {
            return null;
        }

        var foundMultiSegmentReference = false;
        foreach (var candidate in candidates)
        {
            var references = TryReadDatFileReferences(candidate);
            if (!references.WasRead || !references.FileNames.Contains(datFileName, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            if (references.FileNames.Count > 1)
            {
                foundMultiSegmentReference = true;
                continue;
            }

            if (references.FileNames.Count == 1)
            {
                return candidate;
            }
        }

        if (foundMultiSegmentReference)
        {
            return null;
        }

        return candidates[0];
    }

    private static SidecarDatFileReferences TryReadDatFileReferences(string sidecarPath)
    {
        try
        {
            var document = XDocument.Load(sidecarPath);
            var fileNames = document
                .Descendants()
                .Where(element =>
                    string.Equals(element.Name.LocalName, "file", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace((string?)element.Attribute("name")))
                .Select(element => ((string?)element.Attribute("name"))!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new SidecarDatFileReferences(true, fileNames);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return new SidecarDatFileReferences(false, Array.Empty<string>());
        }
    }

    private readonly record struct SidecarDatFileReferences(bool WasRead, IReadOnlyList<string> FileNames);
}
