namespace DatConverter;

public static class FfmpegCommandBuilder
{
    public static IReadOnlyList<string> BuildRemuxArguments(
        string inputPath,
        string outputPath,
        OutputFormat outputFormat,
        FpsOption fps)
    {
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
            "-c:v",
            "copy"
        };

        if (outputFormat.IsMp4())
        {
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
}
