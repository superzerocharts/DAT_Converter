namespace DatConverter;

public sealed class SpotterCleanRemuxPrototype
{
    private static readonly TimeSpan FfmpegTimeout = TimeSpan.FromHours(12);

    private readonly Func<string, string, SpotterDatPayloadExtractionResult> extract;
    private readonly Func<string, IReadOnlyList<string>, TimeSpan, CancellationToken, Task<ProcessRunResult>> runProcessAsync;

    public SpotterCleanRemuxPrototype()
        : this(
            (inputPath, outputPath) => new SpotterDatPayloadExtractor().Extract(inputPath, outputPath),
            (executablePath, arguments, timeout, cancellationToken) =>
                FfmpegProcessRunner.RunAsync(executablePath, arguments, timeout, cancellationToken))
    {
    }

    public SpotterCleanRemuxPrototype(
        Func<string, string, SpotterDatPayloadExtractionResult> extract,
        Func<string, IReadOnlyList<string>, TimeSpan, CancellationToken, Task<ProcessRunResult>> runProcessAsync)
    {
        this.extract = extract;
        this.runProcessAsync = runProcessAsync;
    }

    public async Task<SpotterCleanRemuxPrototypeResult> RunAsync(
        string inputPath,
        string outputPath,
        string ffmpegPath,
        FpsOption fps,
        bool keepTemp,
        CancellationToken cancellationToken)
    {
        var outputFormat = InferOutputFormat(outputPath);
        if (string.IsNullOrWhiteSpace(inputPath) || !string.Equals(Path.GetExtension(inputPath), ".dat", StringComparison.OrdinalIgnoreCase))
        {
            return CreateFailure(inputPath, outputPath, fps, outputFormat ?? OutputFormat.Mp4, "Input must be a .dat file.");
        }

        if (!File.Exists(inputPath))
        {
            return CreateFailure(inputPath, outputPath, fps, outputFormat ?? OutputFormat.Mp4, $"Input file was not found: {inputPath}");
        }

        if (outputFormat is null)
        {
            return CreateFailure(inputPath, outputPath, fps, OutputFormat.Mp4, "Output path must end with .mp4 or .mkv.");
        }

        var resolvedOutputFormat = outputFormat.Value;

        if (File.Exists(outputPath))
        {
            return CreateFailure(inputPath, outputPath, fps, resolvedOutputFormat, $"Output already exists: {outputPath}");
        }

        if (string.IsNullOrWhiteSpace(ffmpegPath) || !File.Exists(ffmpegPath))
        {
            return CreateFailure(inputPath, outputPath, fps, resolvedOutputFormat, $"Bundled FFmpeg was not found: {ffmpegPath}");
        }

        var outputDirectory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        var tempH264Path = Path.Combine(
            string.IsNullOrWhiteSpace(outputDirectory) ? Path.GetTempPath() : outputDirectory,
            $"{Path.GetFileNameWithoutExtension(outputPath)}.{Guid.NewGuid():N}.prototype.h264");

        SpotterDatPayloadExtractionResult? extractionResult = null;
        IReadOnlyList<string> arguments = Array.Empty<string>();
        ProcessRunResult? ffmpegResult = null;
        try
        {
            extractionResult = extract(inputPath, tempH264Path);
            if (!extractionResult.Succeeded || !extractionResult.LookedConfident)
            {
                return new SpotterCleanRemuxPrototypeResult
                {
                    Succeeded = false,
                    InputPath = inputPath,
                    OutputPath = outputPath,
                    TempH264Path = keepTemp && File.Exists(tempH264Path) ? tempH264Path : null,
                    KeptTempFile = keepTemp && File.Exists(tempH264Path),
                    FpsValue = fps.FfmpegValue,
                    OutputFormat = resolvedOutputFormat,
                    ExtractionResult = extractionResult,
                    FailureReason = "Extraction was not confident enough to remux."
                };
            }

            arguments = BuildCleanRemuxArguments(tempH264Path, outputPath, resolvedOutputFormat, fps);
            ffmpegResult = await runProcessAsync(ffmpegPath, arguments, FfmpegTimeout, cancellationToken);
            if (ffmpegResult.ExitCode != 0 || !File.Exists(outputPath))
            {
                TryDeleteFile(outputPath);
                return new SpotterCleanRemuxPrototypeResult
                {
                    Succeeded = false,
                    InputPath = inputPath,
                    OutputPath = outputPath,
                    TempH264Path = keepTemp && File.Exists(tempH264Path) ? tempH264Path : null,
                    KeptTempFile = keepTemp && File.Exists(tempH264Path),
                    FpsValue = fps.FfmpegValue,
                    OutputFormat = resolvedOutputFormat,
                    ExtractionResult = extractionResult,
                    FfmpegArguments = arguments,
                    FfmpegResult = ffmpegResult,
                    FailureReason = "FFmpeg clean remux failed."
                };
            }

            return new SpotterCleanRemuxPrototypeResult
            {
                Succeeded = true,
                InputPath = inputPath,
                OutputPath = outputPath,
                TempH264Path = keepTemp ? tempH264Path : null,
                KeptTempFile = keepTemp,
                FpsValue = fps.FfmpegValue,
                OutputFormat = resolvedOutputFormat,
                ExtractionResult = extractionResult,
                FfmpegArguments = arguments,
                FfmpegResult = ffmpegResult
            };
        }
        finally
        {
            if (!keepTemp)
            {
                TryDeleteFile(tempH264Path);
            }
        }
    }

    public static IReadOnlyList<string> BuildCleanRemuxArguments(
        string h264InputPath,
        string outputPath,
        OutputFormat outputFormat,
        FpsOption fps,
        ContainerMetadata? metadata = null)
    {
        if (string.IsNullOrWhiteSpace(fps.FfmpegValue))
        {
            throw new InvalidOperationException("Source FPS is not set.");
        }

        var arguments = new List<string>
        {
            "-n",
            "-fflags",
            "+genpts+discardcorrupt",
            "-err_detect",
            "ignore_err",
            "-f",
            "h264",
            "-r",
            fps.FfmpegValue,
            "-i",
            h264InputPath,
            "-c:v",
            "copy"
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

    public static OutputFormat? InferOutputFormat(string outputPath)
    {
        return Path.GetExtension(outputPath).ToLowerInvariant() switch
        {
            ".mp4" => OutputFormat.Mp4,
            ".mkv" => OutputFormat.Mkv,
            _ => null
        };
    }

    private static SpotterCleanRemuxPrototypeResult CreateFailure(
        string inputPath,
        string outputPath,
        FpsOption fps,
        OutputFormat outputFormat,
        string reason)
    {
        return new SpotterCleanRemuxPrototypeResult
        {
            Succeeded = false,
            InputPath = inputPath,
            OutputPath = outputPath,
            FpsValue = string.IsNullOrWhiteSpace(fps.FfmpegValue) ? "30" : fps.FfmpegValue,
            OutputFormat = outputFormat,
            FailureReason = reason
        };
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }
}
