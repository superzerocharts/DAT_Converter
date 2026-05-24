namespace DatConverter;

public static class QueueItemStatusService
{
    public static bool HasReusableProbeForCurrentFps(QueueItem item)
    {
        return item.PreProbeResult?.IsSuccess == true &&
               string.Equals(item.PreProbeResult.Fps.FfmpegValue, item.FfmpegRateValue, StringComparison.Ordinal);
    }

    public static void ApplyPostFpsResolutionStatus(QueueItem item)
    {
        if (item.HasExistingDirectOutput)
        {
            item.ResultStatusSummary = "Skipped - output already exists";
            item.Status = QueueItemStatus.Skipped;
            item.StatusText = "Exists";
            item.ProgressText = "Selected output exists";
            return;
        }

        if (item.RequiresManualFpsSelection || !item.HasResolvedFps)
        {
            item.PreProbeResult = null;
            item.Status = QueueItemStatus.Warning;
            item.StatusText = "Needs FPS";
            item.ProgressText = "Choose Source FPS";
        }
    }

    public static void ApplyPreProbeResult(QueueItem item, ProbeResult probeResult)
    {
        item.PreProbeResult = probeResult;

        if (!probeResult.IsSuccess)
        {
            item.ResultStatusSummary = "Skipped - unsupported video payload";
            item.Status = QueueItemStatus.Unsupported;
            item.StatusText = "Unsupported";
            item.ProgressText = "Will not process";
            return;
        }

        if (item.HasExistingDirectOutput)
        {
            item.ResultStatusSummary = "Skipped - output already exists";
            item.Status = QueueItemStatus.Skipped;
            item.StatusText = "Exists";
            item.ProgressText = "Selected output exists";
            return;
        }

        if (item.RequiresManualFpsSelection || !item.HasResolvedFps)
        {
            item.Status = QueueItemStatus.Warning;
            item.StatusText = "Needs FPS";
            item.ProgressText = "Choose Source FPS";
            return;
        }

        item.ResultStatusSummary = null;
        item.Status = QueueItemStatus.Ready;
        item.StatusText = "Ready";
        item.ProgressText = FormatProbeProgressText(probeResult);
    }

    private static string FormatProbeProgressText(ProbeResult probeResult)
    {
        if (probeResult.Width.HasValue && probeResult.Height.HasValue)
        {
            return $"{probeResult.Width.Value}x{probeResult.Height.Value}";
        }

        return "Ready";
    }
}
