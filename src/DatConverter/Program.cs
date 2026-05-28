namespace DatConverter;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (TryRunDeveloperPrototype(args))
        {
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }

    private static bool TryRunDeveloperPrototype(string[] args)
    {
        if (args.Length == 0)
        {
            return false;
        }

        if (string.Equals(args[0], "--prototype-extract-spotter-h264", StringComparison.OrdinalIgnoreCase))
        {
            RunExtractionPrototype(args);
            return true;
        }

        if (string.Equals(args[0], "--prototype-clean-remux-spotter-dat", StringComparison.OrdinalIgnoreCase))
        {
            RunCleanRemuxPrototypeAsync(args).GetAwaiter().GetResult();
            return true;
        }

        if (string.Equals(args[0], "--prototype-print-split-export-plan", StringComparison.OrdinalIgnoreCase))
        {
            RunSplitExportPlanPrototype(args);
            return true;
        }

        if (string.Equals(args[0], "--prototype-combine-split-export", StringComparison.OrdinalIgnoreCase))
        {
            RunCombinedSplitExportPrototypeAsync(args).GetAwaiter().GetResult();
            return true;
        }

        return false;
    }

    private static void RunExtractionPrototype(string[] args)
    {
        if (args.Length < 2)
        {
            return;
        }

        var inputPath = args[1];
        var outputPath = args.Length >= 3 && !args[2].StartsWith("--", StringComparison.Ordinal)
            ? args[2]
            : Path.ChangeExtension(inputPath, ".clean.prototype.h264");
        var reportPath = Path.ChangeExtension(outputPath, ".report.txt");
        var result = new SpotterDatPayloadExtractor().Extract(inputPath, outputPath);
        File.WriteAllText(reportPath, result.BuildTechnicalReport());
    }

    private static async Task RunCleanRemuxPrototypeAsync(string[] args)
    {
        if (args.Length < 3)
        {
            return;
        }

        var inputPath = args[1];
        var outputPath = args[2];
        var fpsLabel = "30";
        var keepTemp = false;
        for (var index = 3; index < args.Length; index++)
        {
            if (string.Equals(args[index], "--keep-temp", StringComparison.OrdinalIgnoreCase))
            {
                keepTemp = true;
            }
            else if (!args[index].StartsWith("--", StringComparison.Ordinal))
            {
                fpsLabel = args[index];
            }
        }

        var tools = ToolPathService.ResolveBundledTools();
        var result = await new SpotterCleanRemuxPrototype().RunAsync(
            inputPath,
            outputPath,
            tools.FfmpegPath,
            FpsOption.FromLabel(fpsLabel),
            keepTemp,
            CancellationToken.None);
        var reportPath = Path.ChangeExtension(outputPath, ".clean-remux-report.txt");
        File.WriteAllText(reportPath, result.BuildTechnicalReport());
    }

    private static void RunSplitExportPlanPrototype(string[] args)
    {
        if (args.Length < 2)
        {
            return;
        }

        var inputPath = args[1];
        var outputPath = args.Length >= 3 && !args[2].StartsWith("--", StringComparison.Ordinal)
            ? args[2]
            : GetDefaultSplitExportPlanReportPath(inputPath);
        var plan = new SpotterSplitExportPlanBuilder().Build(inputPath);
        File.WriteAllText(outputPath, plan.BuildTechnicalReport());
    }

    private static string GetDefaultSplitExportPlanReportPath(string inputPath)
    {
        if (Directory.Exists(inputPath))
        {
            return Path.Combine(inputPath, "split-export-plan.prototype.txt");
        }

        return Path.ChangeExtension(inputPath, ".split-export-plan.prototype.txt");
    }

    private static async Task RunCombinedSplitExportPrototypeAsync(string[] args)
    {
        if (args.Length < 3)
        {
            return;
        }

        var inputPath = args[1];
        var outputPath = args[2];
        var fpsLabel = "30";
        var keepTemp = false;
        for (var index = 3; index < args.Length; index++)
        {
            if (string.Equals(args[index], "--keep-temp", StringComparison.OrdinalIgnoreCase))
            {
                keepTemp = true;
            }
            else if (!args[index].StartsWith("--", StringComparison.Ordinal))
            {
                fpsLabel = args[index];
            }
        }

        var tools = ToolPathService.ResolveBundledTools();
        var result = await new SpotterCombinedSplitExportPrototype().RunAsync(
            inputPath,
            outputPath,
            tools.FfmpegPath,
            FpsOption.FromLabel(fpsLabel),
            keepTemp,
            CancellationToken.None);
        var reportPath = outputPath + ".combined-split-export-report.txt";
        File.WriteAllText(reportPath, result.BuildTechnicalReport());
    }
}
