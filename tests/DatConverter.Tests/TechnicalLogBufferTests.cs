namespace DatConverter.Tests;

public sealed class TechnicalLogBufferTests
{
    [Fact]
    public void Text_KeepsEntriesOldestToNewest()
    {
        var log = new TechnicalLogBuffer();

        log.Append("First event");
        log.Append("Second event");
        log.Append("Third event");

        var text = log.Text;

        Assert.True(text.IndexOf("First event", StringComparison.Ordinal) < text.IndexOf("Second event", StringComparison.Ordinal));
        Assert.True(text.IndexOf("Second event", StringComparison.Ordinal) < text.IndexOf("Third event", StringComparison.Ordinal));
    }
}
