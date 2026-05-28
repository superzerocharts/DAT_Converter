namespace DatConverter.Tests;

public sealed class BurnTimestampUiPolicyTests
{
    [Fact]
    public void ReliabilityNote_UsesCompactHelperText()
    {
        Assert.Equal("Uses recording time if available.", BurnTimestampMetadataBuilder.ReliabilityNote);
    }

    [Fact]
    public void Evaluate_DisablesAndClearsBurnTimestampInFastMode()
    {
        var state = BurnTimestampUiPolicy.Evaluate("Remux", hasRecordingTime: true, requestedChecked: true);

        Assert.False(state.Enabled);
        Assert.False(state.Checked);
        Assert.Equal(BurnTimestampMetadataBuilder.ReliabilityNote, state.Note);
    }

    [Fact]
    public void Evaluate_EnablesBurnTimestampInFullMode()
    {
        var state = BurnTimestampUiPolicy.Evaluate("Encode", hasRecordingTime: true, requestedChecked: true);

        Assert.True(state.Enabled);
        Assert.True(state.Checked);
        Assert.Equal(BurnTimestampMetadataBuilder.ReliabilityNote, state.Note);
    }

    [Fact]
    public void Evaluate_EnablesBurnTimestampInFullNvencMode()
    {
        var state = BurnTimestampUiPolicy.Evaluate("Full NVENC", hasRecordingTime: true, requestedChecked: true);

        Assert.True(state.Enabled);
        Assert.True(state.Checked);
        Assert.Equal(BurnTimestampMetadataBuilder.ReliabilityNote, state.Note);
    }

    [Fact]
    public void Evaluate_BlocksBurnTimestampWithoutRecordingTime()
    {
        var state = BurnTimestampUiPolicy.Evaluate("Encode", hasRecordingTime: false, requestedChecked: true);

        Assert.False(state.Enabled);
        Assert.False(state.Checked);
        Assert.Equal(BurnTimestampMetadataBuilder.RequiresRecordingDateTimeMessage, state.Note);
    }
}
