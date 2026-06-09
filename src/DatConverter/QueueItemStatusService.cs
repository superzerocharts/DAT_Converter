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
            return;
        }

        if (item.IsCombinedStoryboard)
        {
            ApplyCombinedStoryboardReadiness(item);
        }
    }

    public static bool ApplyCombinedStoryboardReadiness(QueueItem item)
    {
        if (!item.IsCombinedStoryboard)
        {
            return false;
        }

        item.PreProbeResult = null;

        if (item.HasExistingDirectOutput)
        {
            item.ResultStatusSummary = "Skipped - output already exists";
            item.Status = QueueItemStatus.Skipped;
            item.StatusText = "Exists";
            item.ProgressText = "Selected output exists";
            return true;
        }

        if (item.RequiresManualFpsSelection || !item.HasResolvedFps)
        {
            item.Status = QueueItemStatus.Warning;
            item.StatusText = "Needs FPS";
            item.ProgressText = "Choose Source FPS";
            return true;
        }

        var validationMessage = ValidateCombinedStoryboardSources(item);
        if (!string.IsNullOrWhiteSpace(validationMessage))
        {
            item.ResultStatusSummary = "Skipped - invalid storyboard";
            item.Status = QueueItemStatus.Unsupported;
            item.StatusText = "Unsupported";
            item.ProgressText = validationMessage;
            return true;
        }

        item.ResultStatusSummary = null;
        item.Status = QueueItemStatus.Ready;
        item.StatusText = "Ready";
        item.ProgressText = "Ready";
        return true;
    }

    private static string? ValidateCombinedStoryboardSources(QueueItem item)
    {
        var plan = item.StoryboardPlan;
        if (plan is null || !plan.IsStrongConfidence)
        {
            return "Storyboard invalid";
        }

        if (plan.Clips.Count == 0)
        {
            return "Storyboard has no clips";
        }

        foreach (var clip in plan.Clips)
        {
            if (string.IsNullOrWhiteSpace(clip.DatFilePath) || !File.Exists(clip.DatFilePath))
            {
                return $"Missing clip {clip.ClipNumber}";
            }

            try
            {
                if (new FileInfo(clip.DatFilePath).Length <= 0)
                {
                    return $"Empty clip {clip.ClipNumber}";
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                return $"Unreadable clip {clip.ClipNumber}";
            }
        }

        return null;
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
