using System.Diagnostics;
using System.Text;
using WindowsCommander.Core.Models;
using WindowsCommander.Core.Services;
using WindowsCommander.Windows.Native;

namespace WindowsCommander.Windows.Services;

public sealed class WindowService : IWindowService
{
    public IReadOnlyList<WindowSummary> ListWindows()
    {
        return EnumerateWindows(visibleOnly: true)
            .Where(window => !string.IsNullOrWhiteSpace(window.Title))
            .Select(window => new WindowSummary(window.HWND, window.Title, window.OwningPID, window.IsMinimized, window.BoundingRect))
            .ToArray();
    }

    public IReadOnlyList<WindowDetails> FindWindows(string? titleContains, string? className, string? processName, int? pid, bool visibleOnly)
    {
        return EnumerateWindows(visibleOnly)
            .Where(window => Matches(window, titleContains, className, processName, pid))
            .ToArray();
    }

    internal static IReadOnlyList<WindowDetails> EnumerateWindows(bool visibleOnly)
    {
        var windows = new List<WindowDetails>();

        NativeMethods.EnumWindows((hwnd, _) =>
        {
            var isVisible = NativeMethods.IsWindowVisible(hwnd);
            if (visibleOnly && !isVisible)
            {
                return true;
            }

            NativeMethods.GetWindowThreadProcessId(hwnd, out var processId);
            var title = GetWindowText(hwnd);
            var className = GetClassName(hwnd);
            var processName = GetProcessName((int)processId);
            var bounds = GetWindowBounds(hwnd);

            windows.Add(new WindowDetails(
                hwnd,
                title,
                className,
                (int)processId,
                processName,
                isVisible,
                NativeMethods.IsIconic(hwnd),
                bounds));

            return true;
        }, nint.Zero);

        return windows;
    }

    private static bool Matches(WindowDetails window, string? titleContains, string? className, string? processName, int? pid)
    {
        if (!string.IsNullOrWhiteSpace(titleContains) && !window.Title.Contains(titleContains, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(className) && !string.Equals(window.ClassName, className, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(processName) && !window.ProcessName.Contains(processName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return pid is null || window.OwningPID == pid.Value;
    }

    private static string GetWindowText(nint hwnd)
    {
        var builder = new StringBuilder(512);
        _ = NativeMethods.GetWindowText(hwnd, builder, builder.Capacity);
        return builder.ToString();
    }

    private static string GetClassName(nint hwnd)
    {
        var builder = new StringBuilder(256);
        _ = NativeMethods.GetClassName(hwnd, builder, builder.Capacity);
        return builder.ToString();
    }

    private static RectBounds GetWindowBounds(nint hwnd)
    {
        if (!NativeMethods.GetWindowRect(hwnd, out var rect))
        {
            return new RectBounds(0, 0, 0, 0);
        }

        return new RectBounds(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
    }

    private static string GetProcessName(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.ProcessName;
        }
        catch (ArgumentException)
        {
            return string.Empty;
        }
        catch (InvalidOperationException)
        {
            return string.Empty;
        }
    }
}
