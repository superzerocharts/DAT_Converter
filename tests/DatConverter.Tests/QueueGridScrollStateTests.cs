namespace DatConverter.Tests;

public sealed class QueueGridScrollStateTests
{
    [Theory]
    [InlineData(4, 10, 4)]
    [InlineData(12, 10, 9)]
    [InlineData(-1, 10, -1)]
    [InlineData(0, 0, -1)]
    public void CoerceFirstDisplayedRowIndex_PreservesValidIndexAndClampsToRows(
        int firstDisplayedRowIndex,
        int rowCount,
        int expected)
    {
        Assert.Equal(expected, QueueGridScrollState.CoerceFirstDisplayedRowIndex(firstDisplayedRowIndex, rowCount));
    }
}
