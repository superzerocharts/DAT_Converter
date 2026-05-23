using System.Diagnostics;
using System.Text;

namespace DatConverter;

public static class FfmpegProcessRunner
{
    private const int MaxCapturedCharactersPerStream = 200_000;
    private const string TruncationNotice = "[Process output truncated: older lines were removed.]";

    public static async Task<ProcessRunResult> RunAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        Action<string>? standardOutputLineReceived = null,
        Action<string>? standardErrorLineReceived = null)
    {
        using var timeoutSource = new CancellationTokenSource(timeout);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);

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

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var standardOutput = new StringBuilder();
        var standardError = new StringBuilder();
        var stdoutTask = Task.CompletedTask;
        var stderrTask = Task.CompletedTask;

        try
        {
            process.Start();

            stdoutTask = ReadLinesAsync(process.StandardOutput, standardOutput, standardOutputLineReceived);
            stderrTask = ReadLinesAsync(process.StandardError, standardError, standardErrorLineReceived);

            await process.WaitForExitAsync(linkedSource.Token);
            await Task.WhenAll(stdoutTask, stderrTask);

            return new ProcessRunResult(process.ExitCode, false, false, standardOutput.ToString(), standardError.ToString());
        }
        catch (OperationCanceledException)
        {
            var timedOut = timeoutSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested;
            var wasCanceled = cancellationToken.IsCancellationRequested;

            TryKillProcessTree(process);
            await WaitForProcessExitQuietlyAsync(process);
            await AwaitOutputReadersQuietlyAsync(stdoutTask, stderrTask);

            return new ProcessRunResult(null, timedOut, wasCanceled, standardOutput.ToString(), AppendProcessEndMessage(standardError, timedOut ? "Process timed out." : "Process was canceled."));
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            return new ProcessRunResult(null, false, false, standardOutput.ToString(), AppendProcessEndMessage(standardError, ex.Message));
        }
    }

    private static async Task ReadLinesAsync(StreamReader reader, StringBuilder capture, Action<string>? lineReceived)
    {
        try
        {
            while (await reader.ReadLineAsync() is { } line)
            {
            AppendCapturedLine(capture, line);
            lineReceived?.Invoke(line);
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
        }
    }

    private static void TryKillProcessTree(Process process)
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

    private static async Task WaitForProcessExitQuietlyAsync(Process process)
    {
        try
        {
            await process.WaitForExitAsync(CancellationToken.None);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
    }

    private static async Task AwaitOutputReadersQuietlyAsync(Task stdoutTask, Task stderrTask)
    {
        try
        {
            await Task.WhenAll(stdoutTask, stderrTask);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
        }
    }

    private static string AppendProcessEndMessage(StringBuilder standardError, string message)
    {
        AppendCapturedLine(standardError, message);
        return standardError.ToString();
    }

    private static void AppendCapturedLine(StringBuilder capture, string line)
    {
        capture.AppendLine(line);

        if (capture.Length <= MaxCapturedCharactersPerStream)
        {
            return;
        }

        var removeLength = capture.Length - MaxCapturedCharactersPerStream + TruncationNotice.Length + Environment.NewLine.Length;
        removeLength = Math.Min(removeLength, capture.Length);
        capture.Remove(0, removeLength);

        if (!capture.ToString().StartsWith(TruncationNotice, StringComparison.Ordinal))
        {
            capture.Insert(0, TruncationNotice + Environment.NewLine);
        }
    }
}
