using System.Globalization;
using System.Text;

namespace DatConverter;

public static class ContainerMetadataFormatter
{
    public static IReadOnlyList<string> BuildFfmpegArguments(ContainerMetadata? metadata)
    {
        if (metadata is null)
        {
            return Array.Empty<string>();
        }

        var arguments = new List<string>();
        Add(arguments, "creation_time", metadata.CreationTime?.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture));
        Add(arguments, "title", metadata.Title);
        Add(arguments, "comment", metadata.Comment);
        return arguments;
    }

    public static string? Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var character in value.Trim())
        {
            if (char.IsControl(character))
            {
                if (character is '\r' or '\n' or '\t')
                {
                    builder.Append(' ');
                }

                continue;
            }

            builder.Append(character);
        }

        var sanitized = string.Join(" ", builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return string.IsNullOrWhiteSpace(sanitized) ? null : sanitized;
    }

    private static void Add(List<string> arguments, string key, string? value)
    {
        var sanitized = Sanitize(value);
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            return;
        }

        arguments.Add("-metadata");
        arguments.Add($"{key}={sanitized}");
    }
}
