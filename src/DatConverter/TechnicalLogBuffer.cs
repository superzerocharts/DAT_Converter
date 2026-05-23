using System.Reflection;

namespace DatConverter;

public sealed class TechnicalLogBuffer
{
    private const int DefaultMaxCharacters = 200_000;
    private const string TruncationNotice = "[Log truncated: older entries were removed.]";

    private readonly int maxCharacters;
    private readonly List<string> entries = new();
    private bool wasTruncated;

    public TechnicalLogBuffer(int maxCharacters = DefaultMaxCharacters)
    {
        this.maxCharacters = maxCharacters;
    }

    public string Text
    {
        get
        {
            var text = string.Join(Environment.NewLine, entries);
            return wasTruncated ? $"{TruncationNotice}{Environment.NewLine}{text}" : text;
        }
    }

    public void Append(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        entries.Add($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message.Trim()}");
        TrimIfNeeded();
    }

    public void AppendBlock(string title, string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            Append($"{title}: (none)");
            return;
        }

        Append($"{title}:{Environment.NewLine}{content.Trim()}");
    }

    public void Clear()
    {
        entries.Clear();
        wasTruncated = false;
    }

    public static string GetAppVersion()
    {
        return Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
    }

    private void TrimIfNeeded()
    {
        while (entries.Count > 0 && Text.Length > maxCharacters)
        {
            entries.RemoveAt(0);
            wasTruncated = true;
        }
    }
}
