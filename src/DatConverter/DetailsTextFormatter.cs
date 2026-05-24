namespace DatConverter;

public static class DetailsTextFormatter
{
    public static string BuildSectionedText(
        IEnumerable<string> queueSummaryLines,
        IEnumerable<string> selectedItemLines,
        IEnumerable<string> queueItemResultLines,
        string technicalLog)
    {
        var lines = new List<string>();
        AddSection(lines, "Queue Summary", queueSummaryLines);
        AddSection(lines, "Selected Item", selectedItemLines);
        AddSection(lines, "Queue Item Results", queueItemResultLines);
        AddSection(lines, "Technical Log", SplitLines(technicalLog));
        return string.Join(Environment.NewLine, lines);
    }

    public static string AddVisualBottomPadding(string text)
    {
        return text.TrimEnd('\r', '\n') + Environment.NewLine + Environment.NewLine;
    }

    private static void AddSection(List<string> lines, string title, IEnumerable<string> contentLines)
    {
        if (lines.Count > 0)
        {
            lines.Add(string.Empty);
        }

        lines.Add(title);
        var added = false;
        foreach (var line in contentLines)
        {
            lines.Add(line);
            added = true;
        }

        if (!added)
        {
            lines.Add("None");
        }
    }

    private static IEnumerable<string> SplitLines(string text)
    {
        return string.IsNullOrWhiteSpace(text)
            ? Array.Empty<string>()
            : text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
    }
}
