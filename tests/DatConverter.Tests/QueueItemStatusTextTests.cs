namespace DatConverter.Tests;

public sealed class QueueItemStatusTextTests
{
    [Fact]
    public void WaitingStatusText_IsUserFacingRunningAddPendingText()
    {
        Assert.Equal("Waiting...", QueueItemStatusText.Waiting);
    }
}
