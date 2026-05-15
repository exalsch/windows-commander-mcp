namespace WindowsCommander.Core.Models;

public sealed record CommandExecutionResult(
    string StandardOutput,
    string StandardError,
    int? ExitCode,
    TimeSpan ElapsedTime,
    bool TimedOut);

public sealed record ProcessStartResult(
    int ProcessId,
    string? StandardOutput,
    string? StandardError,
    int? ExitCode,
    TimeSpan? ElapsedTime,
    bool TimedOut);
