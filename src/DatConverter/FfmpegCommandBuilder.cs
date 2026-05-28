namespace DatConverter;

public static class FfmpegCommandBuilder
{
    public static IReadOnlyList<string> BuildRemuxArguments(
        string inputPath,
        string outputPath,
        OutputFormat outputFormat,
        FpsOption fps,
        ContainerMetadata? metadata = null,
        BurnTimestampOptions? burnTimestamp = null)
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

        arguments.AddRange(ContainerMetadataFormatter.BuildFfmpegArguments(metadata));
        arguments.Add(outputPath);
        return arguments;
    }

    public static IReadOnlyList<string> BuildEncodeArguments(
        string inputPath,
        string outputPath,
        OutputFormat outputFormat,
        FpsOption fps,
        ContainerMetadata? metadata = null,
        BurnTimestampOptions? burnTimestamp = null)
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
            BuildEncodeVideoFilter(fps, burnTimestamp),
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

        arguments.AddRange(ContainerMetadataFormatter.BuildFfmpegArguments(metadata));
        arguments.Add(outputPath);
        return arguments;
    }

    public static IReadOnlyList<string> BuildNvencEncodeArguments(
        string inputPath,
        string outputPath,
        OutputFormat outputFormat,
        FpsOption fps,
        ContainerMetadata? metadata = null,
        BurnTimestampOptions? burnTimestamp = null)
    {
        ValidateResolvedFps(fps);

        var arguments = BuildEncodeBaseArguments(inputPath, fps);
        arguments.Add("-vf");
        arguments.Add(BuildEncodeVideoFilter(fps, burnTimestamp));
        AddNvencEncodeOptions(arguments);

        if (outputFormat.IsMp4())
        {
            arguments.Add("-movflags");
            arguments.Add("+faststart");
        }

        arguments.AddRange(ContainerMetadataFormatter.BuildFfmpegArguments(metadata));
        arguments.Add(outputPath);
        return arguments;
    }

    public static IReadOnlyList<string> BuildTrimEncodeArguments(
        string inputPath,
        string outputPath,
        OutputFormat outputFormat,
        FpsOption fps,
        TimeSpan preRoll,
        TimeSpan duration,
        ContainerMetadata? metadata = null,
        BurnTimestampOptions? burnTimestamp = null)
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
            "-ss",
            FormatSeconds(preRoll),
            "-t",
            FormatSeconds(duration),
            "-vf",
            BuildTrimEncodeVideoFilter(fps, burnTimestamp),
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

        arguments.AddRange(ContainerMetadataFormatter.BuildFfmpegArguments(metadata));
        arguments.Add(outputPath);
        return arguments;
    }

    public static IReadOnlyList<string> BuildTrimNvencEncodeArguments(
        string inputPath,
        string outputPath,
        OutputFormat outputFormat,
        FpsOption fps,
        TimeSpan preRoll,
        TimeSpan duration,
        ContainerMetadata? metadata = null,
        BurnTimestampOptions? burnTimestamp = null)
    {
        ValidateResolvedFps(fps);

        var arguments = BuildEncodeBaseArguments(inputPath, fps);
        arguments.Add("-ss");
        arguments.Add(FormatSeconds(preRoll));
        arguments.Add("-t");
        arguments.Add(FormatSeconds(duration));
        arguments.Add("-vf");
        arguments.Add(BuildTrimEncodeVideoFilter(fps, burnTimestamp));
        AddNvencEncodeOptions(arguments);

        if (outputFormat.IsMp4())
        {
            arguments.Add("-movflags");
            arguments.Add("+faststart");
        }

        arguments.AddRange(ContainerMetadataFormatter.BuildFfmpegArguments(metadata));
        arguments.Add(outputPath);
        return arguments;
    }

    private static List<string> BuildEncodeBaseArguments(string inputPath, FpsOption fps)
    {
        return new List<string>
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
    }

    private static void AddNvencEncodeOptions(List<string> arguments)
    {
        arguments.Add("-an");
        arguments.Add("-c:v");
        arguments.Add("h264_nvenc");
        arguments.Add("-preset");
        arguments.Add("p1");
        arguments.Add("-cq");
        arguments.Add("23");
        arguments.Add("-b:v");
        arguments.Add("0");
    }

    private static string BuildSetPtsFpsExpression(FpsOption fps)
    {
        return string.Equals(fps.FfmpegValue, "30000/1001", StringComparison.Ordinal)
            ? $"({fps.FfmpegValue})"
            : fps.FfmpegValue;
    }

    private static string BuildEncodeVideoFilter(FpsOption fps, BurnTimestampOptions? burnTimestamp)
    {
        return AppendBurnTimestampFilter(
            $"setpts=N/({BuildSetPtsFpsExpression(fps)}*TB),fps={fps.FfmpegValue},format=yuv420p",
            burnTimestamp);
    }

    private static string BuildTrimEncodeVideoFilter(FpsOption fps, BurnTimestampOptions? burnTimestamp)
    {
        return AppendBurnTimestampFilter(
            $"setpts=PTS-STARTPTS,fps={fps.FfmpegValue},format=yuv420p",
            burnTimestamp);
    }

    private static string AppendBurnTimestampFilter(string baseFilter, BurnTimestampOptions? burnTimestamp)
    {
        if (burnTimestamp is null)
        {
            return baseFilter;
        }

        var epochSeconds = new DateTimeOffset(DateTime.SpecifyKind(burnTimestamp.StartTime, DateTimeKind.Local)).ToUnixTimeSeconds();
        var cameraName = EscapeDrawTextText(burnTimestamp.CameraName);
        var fontFileOption = BuildFontFileOption(burnTimestamp.FontFilePath);
        return baseFilter +
               $",drawtext={fontFileOption}text='{cameraName}':x=20:y=20:fontcolor=white:fontsize=28" +
               $",drawtext={fontFileOption}text='%{{pts\\:localtime\\:{epochSeconds}\\:%m/%d/%y}}':x=20:y=54:fontcolor=white:fontsize=28" +
               $",drawtext={fontFileOption}text='%{{pts\\:localtime\\:{epochSeconds}\\:%H\\\\\\:%M\\\\\\:%S}}':x=20:y=88:fontcolor=white:fontsize=28";
    }

    private static string BuildFontFileOption(string? fontFilePath)
    {
        return string.IsNullOrWhiteSpace(fontFilePath)
            ? ""
            : $"fontfile='{EscapeDrawTextFontFile(fontFilePath)}':";
    }

    private static string EscapeDrawTextFontFile(string value)
    {
        return value
            .Replace("\\", "/", StringComparison.Ordinal)
            .Replace(":", "\\:", StringComparison.Ordinal)
            .Replace("'", "\\'", StringComparison.Ordinal);
    }

    private static string EscapeDrawTextText(string? value)
    {
        var sanitized = ContainerMetadataFormatter.Sanitize(value) ?? "Camera";
        return sanitized
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace(":", "\\:", StringComparison.Ordinal)
            .Replace("'", "\\'", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal);
    }

    private static void ValidateResolvedFps(FpsOption fps)
    {
        if (string.IsNullOrWhiteSpace(fps.FfmpegValue))
        {
            throw new InvalidOperationException("Source FPS is not set. Choose Source FPS before converting.");
        }
    }

    private static string FormatSeconds(TimeSpan value)
    {
        return Math.Max(0, value.TotalSeconds).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
    }
}
