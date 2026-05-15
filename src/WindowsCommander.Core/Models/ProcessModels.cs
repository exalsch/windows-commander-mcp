namespace WindowsCommander.Core.Models;

public sealed record ProcessSummary(
    int PID,
    string ProcessName,
    double MemoryUsageMB,
    string MainWindowTitle);

public sealed record ProcessDetails(
    int PID,
    string ProcessName,
    string? ExecutablePath,
    string Status,
    IReadOnlyList<nint> HWNDs);
