namespace DatConverter;

public sealed record ProcessRunResult(
    int? ExitCode,
    bool TimedOut,
    bool WasCanceled,
    string StandardOutput,
    string StandardError);
