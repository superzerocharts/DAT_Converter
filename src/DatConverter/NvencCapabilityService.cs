using System.Diagnostics;
using System.Globalization;

namespace DatConverter;

public sealed class NvencCapabilityService
{
    private static readonly TimeSpan DetectionTimeout = TimeSpan.FromSeconds(8);
    private readonly Func<string, IReadOnlyList<string>, ProcessRunResult> runProcess;
    private readonly Func<string> createSmokeOutputPath;
    private readonly Func<string, bool> fileExists;
    private readonly Func<string, long> getFileLength;
    private readonly Action<string> deleteFile;

    public NvencCapabilityService()
        : this(
            RunProcess,
            () => Path.Combine(Path.GetTempPath(), $"dat_converter_nvenc_smoke_{Guid.NewGuid():N}.mp4"),
            File.Exists,
            path => File.Exists(path) ? new FileInfo(path).Length : 0,
            TryDeleteFile)
    {
    }

    public NvencCapabilityService(
        Func<string, IReadOnlyList<string>, ProcessRunResult> runProcess,
        Func<string>? createSmokeOutputPath = null,
        Func<string, bool>? fileExists = null,
        Func<string, long>? getFileLength = null,
        Action<string>? deleteFile = null)
    {
        this.runProcess = runProcess;
        this.createSmokeOutputPath = createSmokeOutputPath ?? (() => Path.Combine(Path.GetTempPath(), $"dat_converter_nvenc_smoke_{Guid.NewGuid():N}.mp4"));
        this.fileExists = fileExists ?? File.Exists;
        this.getFileLength = getFileLength ?? (path => File.Exists(path) ? new FileInfo(path).Length : 0);
        this.deleteFile = deleteFile ?? TryDeleteFile;
    }

    public NvencCapabilityResult Detect(FfmpegTools tools)
    {
        var ffmpegExists = tools.FfmpegExists && !string.IsNullOrWhiteSpace(tools.FfmpegPath) && fileExists(tools.FfmpegPath);
        var builder = new NvencDiagnosticBuilder(tools.FfmpegPath, ffmpegExists);

        var nvidiaSmi = RunNvidiaSmi();
        builder.SetNvidiaSmi(nvidiaSmi);

        if (!ffmpegExists)
        {
            return builder.Build(
                isAvailable: false,
                encoderListed: false,
                encoderHelpAvailable: false,
                encoderHelpExitCode: null,
                smokeTestSucceeded: false,
                smokeTestExitCode: null,
                smokeTestCommand: "",
                smokeTestStderrTail: "",
                userFacingStatus: "Full NVENC unavailable: bundled ffmpeg.exe was not found.",
                plainReason: "Bundled ffmpeg.exe was not found.");
        }

        var encoders = runProcess(tools.FfmpegPath, ["-hide_banner", "-encoders"]);
        var encoderText = $"{encoders.StandardOutput}{Environment.NewLine}{encoders.StandardError}";
        var encoderListed = encoderText.Contains("h264_nvenc", StringComparison.OrdinalIgnoreCase);
        builder.SetEncoderListing(encoders, encoderText);
        if (!encoderListed)
        {
            return builder.Build(
                isAvailable: false,
                encoderListed: false,
                encoderHelpAvailable: false,
                encoderHelpExitCode: null,
                smokeTestSucceeded: false,
                smokeTestExitCode: null,
                smokeTestCommand: "",
                smokeTestStderrTail: "",
                userFacingStatus: "Full NVENC unavailable: bundled FFmpeg does not list h264_nvenc.",
                plainReason: "Bundled FFmpeg does not list h264_nvenc.");
        }

        var helpArguments = new[] { "-hide_banner", "-h", "encoder=h264_nvenc" };
        var help = runProcess(tools.FfmpegPath, helpArguments);
        var helpText = $"{help.StandardOutput}{Environment.NewLine}{help.StandardError}".Trim();
        var helpAvailable = help.ExitCode == 0 && helpText.Contains("h264_nvenc", StringComparison.OrdinalIgnoreCase);
        builder.SetEncoderHelp(help, helpText);

        var smokeOutputPath = createSmokeOutputPath();
        var smokeArguments = new[]
        {
            "-y",
            "-hide_banner",
            "-loglevel",
            "verbose",
            "-f",
            "lavfi",
            "-i",
            "testsrc2=size=1280x720:rate=30",
            "-t",
            "3",
            "-c:v",
            "h264_nvenc",
            "-preset",
            "p1",
            "-cq",
            "23",
            "-b:v",
            "0",
            smokeOutputPath
        };
        var smoke = runProcess(tools.FfmpegPath, smokeArguments);
        var smokeBytes = getFileLength(smokeOutputPath);
        var smokeSucceeded = smoke.ExitCode == 0 && fileExists(smokeOutputPath) && smokeBytes > 0;
        var smokeCommand = $"{tools.FfmpegPath} {string.Join(" ", smokeArguments.Select(QuoteArgument))}";
        var smokeStderrTail = Tail(smoke.StandardError, 2000);
        builder.SetSmokeTest(smoke, smokeCommand, smokeOutputPath, smokeBytes, smokeStderrTail);
        deleteFile(smokeOutputPath);

        return builder.Build(
            isAvailable: smokeSucceeded,
            encoderListed: true,
            encoderHelpAvailable: helpAvailable,
            encoderHelpExitCode: help.ExitCode,
            smokeTestSucceeded: smokeSucceeded,
            smokeTestExitCode: smoke.ExitCode,
            smokeTestCommand: smokeCommand,
            smokeTestStderrTail: smokeStderrTail,
            userFacingStatus: smokeSucceeded
                ? "Full NVENC available."
                : "Full NVENC unavailable: h264_nvenc smoke test failed.",
            plainReason: smokeSucceeded
                ? "h264_nvenc smoke test succeeded."
                : "h264_nvenc smoke test failed.");
    }

    private ProcessRunResult RunNvidiaSmi()
    {
        return runProcess("nvidia-smi", ["--query-gpu=name,driver_version", "--format=csv,noheader"]);
    }

    private static ProcessRunResult RunProcess(string executablePath, IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit((int)DetectionTimeout.TotalMilliseconds))
            {
                TryKill(process);
                return new ProcessRunResult(null, true, false, SafeResult(stdoutTask), SafeResult(stderrTask) + Environment.NewLine + "NVENC detection timed out.");
            }

            Task.WaitAll(stdoutTask, stderrTask);
            return new ProcessRunResult(process.ExitCode, false, false, stdoutTask.Result, stderrTask.Result);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            return new ProcessRunResult(null, false, false, "", ex.Message);
        }
    }

    private static string SafeResult(Task<string> task)
    {
        return task.IsCompletedSuccessfully ? task.Result : "";
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
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

    private static string QuoteArgument(string value)
    {
        return value.Contains(' ') || value.Contains(';') || value.Contains(':')
            ? $"\"{value}\""
            : value;
    }

    private static string FormatExitCode(int? exitCode)
    {
        return exitCode?.ToString(CultureInfo.InvariantCulture) ?? "none";
    }

    private static string FormatYesNo(bool value)
    {
        return value ? "yes" : "no";
    }

    private static string FormatProcessText(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "(none)" : value.Trim();
    }

    private static string Tail(string value, int maxCharacters)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "(none)";
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxCharacters ? trimmed : trimmed[^maxCharacters..];
    }

    private static string SummarizeNvidiaSmi(ProcessRunResult result)
    {
        if (result.ExitCode != 0)
        {
            return FormatProcessText(result.StandardError);
        }

        var lines = result.StandardOutput
            .Split([Environment.NewLine, "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(3)
            .ToList();
        return lines.Count == 0 ? "(no GPU rows reported)" : string.Join("; ", lines);
    }

    private sealed class NvencDiagnosticBuilder
    {
        private readonly List<string> lines = new();
        private readonly string ffmpegPath;
        private readonly bool ffmpegExists;
        private ProcessRunResult? nvidiaSmi;
        private string nvidiaSmiSummary = "(not checked)";
        private ProcessRunResult? encoderListing;
        private string encoderListingText = "";
        private ProcessRunResult? encoderHelp;
        private string encoderHelpText = "";
        private ProcessRunResult? smokeTest;
        private string smokeCommand = "";
        private string smokeOutputPath = "";
        private long smokeOutputBytes;
        private string smokeStderrTail = "";

        public NvencDiagnosticBuilder(string ffmpegPath, bool ffmpegExists)
        {
            this.ffmpegPath = ffmpegPath;
            this.ffmpegExists = ffmpegExists;
        }

        public void SetNvidiaSmi(ProcessRunResult result)
        {
            nvidiaSmi = result;
            nvidiaSmiSummary = SummarizeNvidiaSmi(result);
        }

        public void SetEncoderListing(ProcessRunResult result, string text)
        {
            encoderListing = result;
            encoderListingText = text;
        }

        public void SetEncoderHelp(ProcessRunResult result, string text)
        {
            encoderHelp = result;
            encoderHelpText = text;
        }

        public void SetSmokeTest(ProcessRunResult result, string command, string outputPath, long outputBytes, string stderrTail)
        {
            smokeTest = result;
            smokeCommand = command;
            smokeOutputPath = outputPath;
            smokeOutputBytes = outputBytes;
            smokeStderrTail = stderrTail;
        }

        public NvencCapabilityResult Build(
            bool isAvailable,
            bool encoderListed,
            bool encoderHelpAvailable,
            int? encoderHelpExitCode,
            bool smokeTestSucceeded,
            int? smokeTestExitCode,
            string smokeTestCommand,
            string smokeTestStderrTail,
            string userFacingStatus,
            string plainReason)
        {
            lines.Clear();
            lines.Add("NVENC capability diagnostic");
            lines.Add($"ffmpeg path: {ffmpegPath}");
            lines.Add($"ffmpeg exists: {FormatYesNo(ffmpegExists)}");
            lines.Add($"h264_nvenc listed: {FormatYesNo(encoderListed)}");
            lines.Add($"ffmpeg -encoders exit code: {FormatExitCode(encoderListing?.ExitCode)}");
            lines.Add($"h264_nvenc help available: {FormatYesNo(encoderHelpAvailable)}");
            lines.Add($"h264_nvenc help exit code: {FormatExitCode(encoderHelpExitCode)}");
            lines.Add($"nvidia-smi available: {FormatYesNo(nvidiaSmi?.ExitCode == 0)}");
            lines.Add($"nvidia-smi exit code: {FormatExitCode(nvidiaSmi?.ExitCode)}");
            lines.Add($"nvidia-smi output summary: {nvidiaSmiSummary}");
            lines.Add($"smoke test command: {FormatProcessText(smokeTestCommand)}");
            lines.Add($"smoke test output: {FormatProcessText(smokeOutputPath)}");
            lines.Add($"smoke test output bytes: {smokeOutputBytes}");
            lines.Add($"smoke test exit code: {FormatExitCode(smokeTestExitCode)}");
            lines.Add($"smoke test stderr tail: {FormatProcessText(smokeTestStderrTail)}");
            lines.Add($"final app decision: Full NVENC {(isAvailable ? "enabled" : "disabled")}");
            lines.Add($"plain reason: {plainReason}");

            if (!string.IsNullOrWhiteSpace(encoderListingText))
            {
                lines.Add("ffmpeg -encoders output tail:");
                lines.Add(Tail(encoderListingText, 2000));
            }

            if (!string.IsNullOrWhiteSpace(encoderHelpText))
            {
                lines.Add("ffmpeg -h encoder=h264_nvenc output tail:");
                lines.Add(Tail(encoderHelpText, 2000));
            }

            if (smokeTest is not null && !string.IsNullOrWhiteSpace(smokeTest.StandardOutput))
            {
                lines.Add("smoke test stdout tail:");
                lines.Add(Tail(smokeTest.StandardOutput, 2000));
            }

            return new NvencCapabilityResult(
                isAvailable,
                encoderListed,
                encoderHelpAvailable,
                nvidiaSmi?.ExitCode == 0,
                smokeTestSucceeded,
                encoderHelpExitCode,
                smokeTestExitCode,
                smokeTestCommand,
                smokeTestStderrTail,
                userFacingStatus,
                plainReason,
                string.Join(Environment.NewLine, lines));
        }
    }
}

public sealed record NvencCapabilityResult(
    bool IsAvailable,
    bool EncoderListed,
    bool EncoderHelpAvailable,
    bool NvidiaSmiAvailable,
    bool SmokeTestSucceeded,
    int? EncoderHelpExitCode,
    int? SmokeTestExitCode,
    string SmokeTestCommand,
    string SmokeTestStderrTail,
    string UserFacingStatus,
    string PlainReason,
    string TechnicalDetails)
{
    public string DiagnosticSummary => UserFacingStatus;
}
