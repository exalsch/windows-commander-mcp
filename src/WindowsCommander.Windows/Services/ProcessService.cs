using System.ComponentModel;
using System.Diagnostics;
using WindowsCommander.Core.Models;
using WindowsCommander.Core.Services;

namespace WindowsCommander.Windows.Services;

public sealed class ProcessService : IProcessService
{
    public IReadOnlyList<ProcessSummary> ListProcesses(string? filterName, bool sortByMemory)
    {
        var processes = Process.GetProcesses()
            .Where(process => MatchesFilter(process, filterName))
            .Select(ToSummary)
            .ToArray();

        return sortByMemory
            ? processes.OrderByDescending(process => process.MemoryUsageMB).ToArray()
            : processes.OrderBy(process => process.ProcessName, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public ProcessDetails GetProcessDetails(int pid)
    {
        using var process = Process.GetProcessById(pid);
        var windows = WindowService.EnumerateWindows(visibleOnly: false)
            .Where(window => window.OwningPID == pid)
            .Select(window => window.HWND)
            .ToArray();

        return new ProcessDetails(
            process.Id,
            process.ProcessName,
            GetExecutablePath(process),
            process.Responding ? "Responding" : "Hung",
            windows);
    }

    public ProcessActionResult ManageProcess(int pid, string action)
    {
        using var process = Process.GetProcessById(pid);
        switch (action.ToLowerInvariant())
        {
            case "terminate":
            case "kill":
                process.Kill(entireProcessTree: false);
                break;
            case "kill_tree":
                process.Kill(entireProcessTree: true);
                break;
            case "close_main_window":
                if (!process.CloseMainWindow())
                {
                    throw new InvalidOperationException($"Process does not have a closable main window: {pid}");
                }

                break;
            default:
                throw new ArgumentException($"Unsupported process action: {action}");
        }

        return new ProcessActionResult(pid, action, Completed: true);
    }

    private static bool MatchesFilter(Process process, string? filterName)
    {
        return string.IsNullOrWhiteSpace(filterName)
            || process.ProcessName.Contains(filterName, StringComparison.OrdinalIgnoreCase);
    }

    private static ProcessSummary ToSummary(Process process)
    {
        using (process)
        {
            return new ProcessSummary(
                process.Id,
                process.ProcessName,
                Math.Round(process.WorkingSet64 / 1024d / 1024d, 2),
                GetMainWindowTitle(process));
        }
    }

    private static string GetMainWindowTitle(Process process)
    {
        try
        {
            return process.MainWindowTitle;
        }
        catch (InvalidOperationException)
        {
            return string.Empty;
        }
    }

    private static string? GetExecutablePath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch (Win32Exception)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
