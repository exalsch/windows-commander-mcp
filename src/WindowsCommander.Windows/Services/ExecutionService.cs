using System.Diagnostics;
using WindowsCommander.Core.Models;
using WindowsCommander.Core.Services;

namespace WindowsCommander.Windows.Services;

public sealed class ExecutionService : IExecutionService
{
    public Task<CommandExecutionResult> ExecutePowerShellAsync(
        string command,
        string? workingDirectory,
        int? timeoutMs,
        IReadOnlyDictionary<string, string>? environment,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            throw new ArgumentException("PowerShell command must not be empty.", nameof(command));
        }

        return ExecuteAndCaptureAsync(
            "pwsh.exe",
            new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-Command", command },
            workingDirectory,
            timeoutMs,
            environment,
            cancellationToken);
    }

    public async Task<ProcessStartResult> ExecuteProcessAsync(
        string executablePath,
        IReadOnlyList<string>? arguments,
        string? workingDirectory,
        int? timeoutMs,
        bool waitForExit,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new ArgumentException("Executable path must not be empty.", nameof(executablePath));
        }

        if (!waitForExit)
        {
            using var process = StartProcess(executablePath, arguments ?? Array.Empty<string>(), workingDirectory, null, redirectOutput: false);
            return new ProcessStartResult(process.Id, null, null, null, null, TimedOut: false);
        }

        var result = await ExecuteAndCaptureAsync(executablePath, arguments ?? Array.Empty<string>(), workingDirectory, timeoutMs, null, cancellationToken);
        return new ProcessStartResult(0, result.StandardOutput, result.StandardError, result.ExitCode, result.ElapsedTime, result.TimedOut);
    }

    private static async Task<CommandExecutionResult> ExecuteAndCaptureAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        int? timeoutMs,
        IReadOnlyDictionary<string, string>? environment,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        using var timeoutSource = timeoutMs is > 0
            ? new CancellationTokenSource(timeoutMs.Value)
            : new CancellationTokenSource();
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
        using var process = StartProcess(executablePath, arguments, workingDirectory, environment, redirectOutput: true);

        try
        {
            var stdoutTask = process.StandardOutput.ReadToEndAsync(linkedSource.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(linkedSource.Token);
            await process.WaitForExitAsync(linkedSource.Token);
            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            stopwatch.Stop();

            return new CommandExecutionResult(stdout, stderr, process.ExitCode, stopwatch.Elapsed, TimedOut: false);
        }
        catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested)
        {
            TryKill(process);
            stopwatch.Stop();
            return new CommandExecutionResult(string.Empty, "Process timed out.", null, stopwatch.Elapsed, TimedOut: true);
        }
    }

    private static Process StartProcess(
        string executablePath,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        IReadOnlyDictionary<string, string>? environment,
        bool redirectOutput)
    {
        var startInfo = new ProcessStartInfo(executablePath)
        {
            UseShellExecute = false,
            RedirectStandardOutput = redirectOutput,
            RedirectStandardError = redirectOutput,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            startInfo.WorkingDirectory = workingDirectory;
        }

        if (environment is not null)
        {
            foreach (var pair in environment)
            {
                startInfo.Environment[pair.Key] = pair.Value;
            }
        }

        return Process.Start(startInfo) ?? throw new InvalidOperationException($"Failed to start process: {executablePath}");
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
        catch (InvalidOperationException ex)
        {
            Console.Error.WriteLine($"Failed to kill process: {ex.Message}");
        }
    }
}
