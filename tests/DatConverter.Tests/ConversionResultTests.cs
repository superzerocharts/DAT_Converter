namespace DatConverter.Tests;

public sealed class ConversionResultTests
{
    [Fact]
    public void StatusSummary_CompletedUsesInWithSeconds()
    {
        var result = CreateResult(
            isSuccess: true,
            processingTime: TimeSpan.FromSeconds(4.24));

        Assert.Equal("Status: Completed in 4.2 seconds", result.StatusSummary);
    }

    [Fact]
    public void StatusSummary_FailedUsesAfterWithSeconds()
    {
        var result = CreateResult(
            isSuccess: false,
            processingTime: TimeSpan.FromSeconds(5.64));

        Assert.Equal("Status: Failed after 5.6 seconds", result.StatusSummary);
    }

    [Fact]
    public void StatusSummary_CanceledUsesAfterWithSeconds()
    {
        var result = CreateResult(
            isSuccess: false,
            wasCanceled: true,
            processingTime: TimeSpan.FromSeconds(2.84));

        Assert.Equal("Status: Canceled after 2.8 seconds", result.StatusSummary);
    }

    [Fact]
    public void StatusSummary_TimedOutUsesAfterWithSeconds()
    {
        var result = CreateResult(
            isSuccess: false,
            timedOut: true,
            processingTime: TimeSpan.FromSeconds(30));

        Assert.Equal("Status: Timed out after 30.0 seconds", result.StatusSummary);
    }

    [Fact]
    public void StatusSummary_UsesClockFormatForLongProcessingTime()
    {
        var result = CreateResult(
            isSuccess: true,
            processingTime: new TimeSpan(1, 2, 45));

        Assert.Equal("Status: Completed in 1:02:45", result.StatusSummary);
    }

    [Fact]
    public void StatusSummary_DoesNotUseVideoDuration()
    {
        var result = CreateResult(
            isSuccess: true,
            duration: TimeSpan.FromMinutes(5),
            processingTime: TimeSpan.FromSeconds(4.2));

        Assert.Equal("Status: Completed in 4.2 seconds", result.StatusSummary);
    }

    private static ConversionResult CreateResult(
        bool isSuccess,
        TimeSpan? processingTime,
        bool wasCanceled = false,
        bool timedOut = false,
        TimeSpan? duration = null)
    {
        return new ConversionResult(
            isSuccess,
            isSuccess ? "Completed." : "Failed.",
            "ffmpeg.exe",
            Array.Empty<string>(),
            "input.dat",
            "output.mp4",
            FpsOption.FromLabel("30"),
            isSuccess ? 0 : 1,
            "",
            "",
            ConversionMode: "Fast",
            OutputFormat: "MP4",
            WasCanceled: wasCanceled,
            TimedOut: timedOut,
            Duration: duration,
            ProcessingTime: processingTime);
    }
}
