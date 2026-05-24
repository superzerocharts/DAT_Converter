namespace DatConverter;

public static class FfmpegCommandBuilder
{
    public static IReadOnlyList<string> BuildRemuxArguments(
        string inputPath,
        string outputPath,
        OutputFormat outputFormat,
        FpsOption fps)
    {
        ValidateResolvedFps(fps);

        var arguments = new List<string>
        {
            "-n",
            "-nostats",
            "-progress",
            "pipe:1",
            "-fflags",
            "+genpts+discardcorrupt",
            "-err_detect",
            "ignore_err",
            "-f",
            "h264",
            "-r",
            fps.FfmpegValue,
            "-i",
            inputPath
        };

        if (outputFormat.IsMp4())
        {
            arguments.Add("-map");
            arguments.Add("0:v:0");
            arguments.Add("-an");
            arguments.Add("-sn");
            arguments.Add("-dn");
        }

        arguments.Add("-c:v");
        arguments.Add("copy");

        if (outputFormat.IsMp4())
        {
            arguments.Add("-tag:v");
            arguments.Add("avc1");
            arguments.Add("-avoid_negative_ts");
            arguments.Add("make_zero");
            arguments.Add("-video_track_timescale");
            arguments.Add("90000");
            arguments.Add("-movflags");
            arguments.Add("+faststart");
        }

        arguments.Add(outputPath);
        return arguments;
    }

    public static IReadOnlyList<string> BuildEncodeArguments(
        string inputPath,
        string outputPath,
        OutputFormat outputFormat,
        FpsOption fps)
    {
        ValidateResolvedFps(fps);

        var arguments = new List<string>
        {
            "-n",
            "-nostats",
            "-progress",
            "pipe:1",
            "-fflags",
            "+genpts+discardcorrupt",
            "-err_detect",
            "ignore_err",
            "-f",
            "h264",
            "-r",
            fps.FfmpegValue,
            "-i",
            inputPath,
            "-vf",
            $"setpts=N/({BuildSetPtsFpsExpression(fps)}*TB),fps={fps.FfmpegValue},format=yuv420p",
            "-an",
            "-c:v",
            "libx264",
            "-preset",
            "veryfast",
            "-crf",
            "22"
        };

        if (outputFormat.IsMp4())
        {
            arguments.Add("-movflags");
            arguments.Add("+faststart");
        }

        arguments.Add(outputPath);
        return arguments;
    }

    private static string BuildSetPtsFpsExpression(FpsOption fps)
    {
        return string.Equals(fps.FfmpegValue, "30000/1001", StringComparison.Ordinal)
            ? $"({fps.FfmpegValue})"
            : fps.FfmpegValue;
    }

    private static void ValidateResolvedFps(FpsOption fps)
    {
        if (string.IsNullOrWhiteSpace(fps.FfmpegValue))
        {
            throw new InvalidOperationException("Source FPS is not set. Choose Source FPS before converting.");
        }
    }
}
