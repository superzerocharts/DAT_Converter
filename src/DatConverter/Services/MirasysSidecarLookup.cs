using System.Xml.Linq;

namespace DatConverter;

public static class MirasysSidecarLookup
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

        foreach (var candidate in candidates)
        {
            if (SidecarReferencesDatFile(candidate, datFileName))
            {
                return candidate;
            }
        }

        return candidates[0];
    }

    private static bool SidecarReferencesDatFile(string sidecarPath, string datFileName)
    {
        try
        {
            var document = XDocument.Load(sidecarPath);
            return document
                .Descendants()
                .Any(element =>
                    string.Equals(element.Name.LocalName, "file", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals((string?)element.Attribute("name"), datFileName, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return false;
        }
    }
}
