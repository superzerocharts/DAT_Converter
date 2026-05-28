using System.Globalization;

namespace DatConverter;

public sealed class FpsDecisionPolicy
{
    private static readonly SupportedRate[] SupportedRates =
    {
        new(15, "15"),
        new(20, "20"),
        new(24, "24"),
        new(25, "25"),
        new(29.97, "30000/1001"),
        new(30, "30")
    };

    public FpsDecisionResult Decide(SpotterFpsDetectionResult detection)
    {
        var warnings = detection.TechnicalDetails.Warnings.ToList();
        var technicalLogText = detection.BuildTechnicalLogText();

        if (!detection.Succeeded)
        {
            return CreateFallback(detection, warnings, technicalLogText, detection.FailureReason);
        }

        var details = detection.TechnicalDetails;
        var bucketEvidence = details.BucketModeFps ?? details.BucketMedianFps;
        var selected = TrySelectNominalRate(bucketEvidence, details.AverageFps, detection.DetectionSource, out var reason);
        var confidence = DetermineConfidence(detection, details, selected, warnings, reason);

        if (selected is null || confidence == "Low")
        {
            return CreateFallback(detection, warnings, technicalLogText, reason);
        }

        return new FpsDecisionResult
        {
            AutoDetectionSucceeded = true,
            ShouldUseDetectedRate = true,
            RawAverageFps = details.AverageFps,
            RawBucketMedianFps = details.BucketMedianFps,
            NominalConversionFps = selected.Value.Fps,
            FfmpegRateValue = selected.Value.FfmpegValue,
            UserFacingLabel = $"Auto {FormatNominalFps(selected.Value.Fps)} fps",
            Confidence = confidence,
            DecisionReason = reason,
            TechnicalLogText = technicalLogText,
            Warnings = warnings
        };
    }

    private static FpsDecisionResult CreateFallback(
        SpotterFpsDetectionResult detection,
        IReadOnlyList<string> warnings,
        string technicalLogText,
        string reason)
    {
        return new FpsDecisionResult
        {
            AutoDetectionSucceeded = detection.Succeeded,
            ShouldUseDetectedRate = false,
            RawAverageFps = detection.TechnicalDetails.AverageFps,
            RawBucketMedianFps = detection.TechnicalDetails.BucketMedianFps,
            NominalConversionFps = 30,
            FfmpegRateValue = "30",
            UserFacingLabel = "30 fps",
            Confidence = "Low",
            DecisionReason = string.IsNullOrWhiteSpace(reason)
                ? "FPS auto-detection was unavailable or uncertain; using the default/manual fallback rate."
                : reason,
            TechnicalLogText = technicalLogText,
            Warnings = warnings
        };
    }

    private static SupportedRate? TrySelectNominalRate(
        double? bucketEvidence,
        double? averageFps,
        string detectionSource,
        out string reason)
    {
        if (!bucketEvidence.HasValue)
        {
            reason = "No stable per-second bucket evidence was available.";
            return null;
        }

        if (IsNtscEvidence(bucketEvidence.Value, averageFps, detectionSource))
        {
            reason = "Sidecar-calibrated bucket and average evidence support NTSC 29.97 fps.";
            return SupportedRates.First(static rate => Math.Abs(rate.Fps - 29.97) < 0.001);
        }

        var bucketRate = FindSupportedRate(bucketEvidence.Value, tolerance: 0.35);
        if (bucketRate is null)
        {
            reason = $"Stable bucket evidence ({bucketEvidence.Value:0.###} fps) did not match a supported nominal FPS.";
            return null;
        }

        if (Math.Abs(bucketRate.Value.Fps - 29.97) < 0.001 && averageFps.HasValue && averageFps.Value < 29.85)
        {
            reason = "Evidence was near 30 fps but the average was too low to confidently choose 29.97.";
            return null;
        }

        var averageText = averageFps.HasValue
            ? averageFps.Value.ToString("0.###", CultureInfo.InvariantCulture)
            : "unknown";
        reason = $"Stable bucket evidence supports {FormatNominalFps(bucketRate.Value.Fps)} fps; average FPS is {averageText}.";
        return bucketRate;
    }

    private static string DetermineConfidence(
        SpotterFpsDetectionResult detection,
        SpotterFpsTechnicalDetails details,
        SupportedRate? selected,
        List<string> warnings,
        string reason)
    {
        if (selected is null || details.FrameCount < 60 || details.BucketCount < 3)
        {
            return "Low";
        }

        var bucketMedian = details.BucketMedianFps;
        if (bucketMedian.HasValue && Math.Abs(bucketMedian.Value - selected.Value.Fps) > 0.5)
        {
            warnings.Add("Bucket median and selected nominal FPS disagree.");
            return "Low";
        }

        if (details.AverageFps.HasValue && Math.Abs(details.AverageFps.Value - selected.Value.Fps) > 1.25)
        {
            warnings.Add("Average FPS and bucket FPS evidence disagree heavily.");
            return "Low";
        }

        if (details.StableBucketCounts.Count > 0 && details.BucketMinFps.HasValue && details.BucketMaxFps.HasValue)
        {
            var spread = details.BucketMaxFps.Value - details.BucketMinFps.Value;
            if (spread > 6)
            {
                warnings.Add("Stable per-second bucket counts are highly variable.");
                return "Low";
            }
        }

        if (details.MultipleResolutionsDetected)
        {
            return "Low";
        }

        _ = reason;
        return string.Equals(detection.Confidence, "High", StringComparison.Ordinal) ? "High" : "Medium";
    }

    private static SupportedRate? FindSupportedRate(double fps, double tolerance)
    {
        SupportedRate? best = null;
        var bestDistance = double.MaxValue;

        foreach (var rate in SupportedRates)
        {
            var distance = Math.Abs(fps - rate.Fps);
            if (distance <= tolerance && distance < bestDistance)
            {
                best = rate;
                bestDistance = distance;
            }
        }

        return best;
    }

    private static bool IsNtscEvidence(double bucketEvidence, double? averageFps, string detectionSource)
    {
        return string.Equals(detectionSource, "DatFrameRecordsWithSefDuration", StringComparison.Ordinal) &&
            bucketEvidence is >= 29.5 and <= 30.5 &&
            averageFps.HasValue &&
            Math.Abs(averageFps.Value - 29.97) <= 0.05;
    }

    private static string FormatNominalFps(double fps)
    {
        return Math.Abs(fps - Math.Round(fps)) < 0.001
            ? Math.Round(fps).ToString("0", CultureInfo.InvariantCulture)
            : fps.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private readonly record struct SupportedRate(double Fps, string FfmpegValue);
}
