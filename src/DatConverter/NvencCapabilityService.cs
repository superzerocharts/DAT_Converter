using System.Diagnostics;

namespace DatConverter;

public sealed class NvencCapabilityService
{
    private static readonly TimeSpan DetectionTimeout = TimeSpan.FromSeconds(5);

    public NvencCapabilityResult Detect(FfmpegTools tools)
    {
        if (!tools.FfmpegExists || string.IsNullOrWhiteSpace(tools.FfmpegPath) || !File.Exists(tools.FfmpegPath))
        {
            return new NvencCapabilityResult(false, false, false, "Bundled ffmpeg.exe was not found.", "");
        }

        var encoders = RunFfmpeg(tools.FfmpegPath, ["-hide_banner", "-encoders"]);
        var encoderText = $"{encoders.StandardOutput}{Environment.NewLine}{encoders.StandardError}";
        var found = encoderText.Contains("h264_nvenc", StringComparison.OrdinalIgnoreCase);
        if (!found)
        {
            return new NvencCapabilityResult(
                false,
                false,
                false,
                $"h264_nvenc was not listed by ffmpeg -encoders. Exit code: {FormatExitCode(encoders.ExitCode)}.",
                encoderText.Trim());
        }

        var help = RunFfmpeg(tools.FfmpegPath, ["-hide_banner", "-h", "encoder=h264_nvenc"]);
        var helpText = $"{help.StandardOutput}{Environment.NewLine}{help.StandardError}".Trim();
        var helpAvailable = help.ExitCode == 0 && helpText.Contains("h264_nvenc", StringComparison.OrdinalIgnoreCase);
        var runtime = RunFfmpeg(tools.FfmpegPath, [
            "-hide_banner",
            "-f",
            "lavfi",
            "-i",
            "color=size=16x16:rate=1:duration=0.1",
            "-frames:v",
            "1",
            "-c:v",
            "h264_nvenc",
            "-preset",
            "p1",
            "-cq",
            "23",
            "-b:v",
            "0",
            "-f",
            "null",
            "NUL"
        ]);
        var runtimeText = $"{runtime.StandardOutput}{Environment.NewLine}{runtime.StandardError}".Trim();
        var runtimeAvailable = runtime.ExitCode == 0;
        var details = string.Join(
            Environment.NewLine + Environment.NewLine,
            new[]
            {
                "ffmpeg -encoders:",
                encoderText.Trim(),
                "ffmpeg -h encoder=h264_nvenc:",
                helpText,
                "NVENC runtime smoke test:",
                runtimeText
            }.Where(value => !string.IsNullOrWhiteSpace(value)));

        return new NvencCapabilityResult(
            helpAvailable && runtimeAvailable,
            true,
            helpAvailable,
            helpAvailable && runtimeAvailable
                ? "h264_nvenc is listed by bundled FFmpeg, encoder help is available, and the runtime smoke test passed."
                : $"h264_nvenc is listed by bundled FFmpeg, but runtime availability was not confirmed. Help exit code: {FormatExitCode(help.ExitCode)}; smoke test exit code: {FormatExitCode(runtime.ExitCode)}.",
            details);
    }

    private static ProcessRunResult RunFfmpeg(string ffmpegPath, IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpegPath,
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

    private static string FormatExitCode(int? exitCode)
    {
        return exitCode?.ToString() ?? "none";
    }
}

public sealed record NvencCapabilityResult(
    bool IsAvailable,
    bool EncoderListed,
    bool EncoderHelpAvailable,
    string DiagnosticSummary,
    string TechnicalDetails);
