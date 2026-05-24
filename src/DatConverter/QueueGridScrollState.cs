namespace DatConverter;

public static class QueueGridScrollState
{
    public static int CoerceFirstDisplayedRowIndex(int firstDisplayedRowIndex, int rowCount)
    {
        if (firstDisplayedRowIndex < 0 || rowCount <= 0)
        {
            return -1;
        }

        return Math.Min(firstDisplayedRowIndex, rowCount - 1);
    }
}
