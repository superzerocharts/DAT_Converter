namespace DatConverter.Tests;

public sealed class DetailsTextFormatterTests
{
    [Fact]
    public void BuildSectionedText_UsesRequestedSectionOrder()
    {
        var text = DetailsTextFormatter.BuildSectionedText(
            ["Status: Completed"],
            ["Input file: selected.dat"],
            ["1 of 1 - Completed in 0.5 seconds - selected.dat"],
            "[2026-05-23 15:35:12] First log line");

        Assert.True(text.IndexOf("Queue Summary", StringComparison.Ordinal) < text.IndexOf("Selected Item", StringComparison.Ordinal));
        Assert.True(text.IndexOf("Selected Item", StringComparison.Ordinal) < text.IndexOf("Queue Item Results", StringComparison.Ordinal));
        Assert.True(text.IndexOf("Queue Item Results", StringComparison.Ordinal) < text.IndexOf("Technical Log", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildSectionedText_QueueItemResultsIncludesAllProvidedItems()
    {
        var text = DetailsTextFormatter.BuildSectionedText(
            ["Status: Completed"],
            ["No queue row selected."],
            [
                "1 of 3 - Completed in 0.5 seconds - first.dat",
                "2 of 3 - Skipped - unsupported video payload - helper.dat",
                "3 of 3 - Completed in 0.6 seconds - third.dat"
            ],
            "log");

        Assert.Contains("1 of 3 - Completed in 0.5 seconds - first.dat", text);
        Assert.Contains("2 of 3 - Skipped - unsupported video payload - helper.dat", text);
        Assert.Contains("3 of 3 - Completed in 0.6 seconds - third.dat", text);
    }

    [Fact]
    public void BuildSectionedText_PreservesTechnicalLogChronology()
    {
        var text = DetailsTextFormatter.BuildSectionedText(
            ["Status: Completed"],
            ["No queue row selected."],
            ["No queued items."],
            "[2026-05-23 15:35:12] First\r\n[2026-05-23 15:35:13] Second\r\n[2026-05-23 15:35:14] Third");

        Assert.True(text.IndexOf("First", StringComparison.Ordinal) < text.IndexOf("Second", StringComparison.Ordinal));
        Assert.True(text.IndexOf("Second", StringComparison.Ordinal) < text.IndexOf("Third", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildSectionedText_DoesNotMoveTechnicalCommandIntoSelectedItem()
    {
        var text = DetailsTextFormatter.BuildSectionedText(
            ["Status: Completed"],
            ["Input file: selected.dat"],
            ["1 of 1 - Completed in 0.5 seconds - selected.dat"],
            "[2026-05-23 15:35:12] FFmpeg command: ffmpeg.exe -i other.dat other.mp4");

        var selectedItemStart = text.IndexOf("Selected Item", StringComparison.Ordinal);
        var queueResultsStart = text.IndexOf("Queue Item Results", StringComparison.Ordinal);
        var selectedItemSection = text[selectedItemStart..queueResultsStart];

        Assert.DoesNotContain("FFmpeg command", selectedItemSection);
        Assert.Contains("FFmpeg command", text);
    }

    [Fact]
    public void AddVisualBottomPadding_AddsExactlyTwoTrailingLineBreaks()
    {
        var text = DetailsTextFormatter.AddVisualBottomPadding("Queue Summary");

        Assert.Equal("Queue Summary" + Environment.NewLine + Environment.NewLine, text);
    }

    [Fact]
    public void AddVisualBottomPadding_TrimsExistingTrailingBlankLinesBeforePadding()
    {
        var text = DetailsTextFormatter.AddVisualBottomPadding("Queue Summary\r\n\r\n\r\n");

        Assert.Equal("Queue Summary" + Environment.NewLine + Environment.NewLine, text);
    }
}
