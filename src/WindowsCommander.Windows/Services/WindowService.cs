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

    public IReadOnlyList<WindowDetails> FindWindows(string? titleContains, string? className, string? processName, int? pid, bool visibleOnly, bool processNameExact = false)
    {
        return EnumerateWindows(visibleOnly)
            .Where(window => Matches(window, titleContains, className, processName, pid, processNameExact))
            .ToArray();
    }

    public WindowActionResult FocusWindow(long windowHandle)
    {
        var handle = new IntPtr(windowHandle);
        var completed = ForceForeground(handle);
        return new WindowActionResult(windowHandle, "focus", completed, GetWindowBounds(handle), null);
    }

    private static bool ForceForeground(IntPtr handle)
    {
        const int swRestore = 9;
        const int swShow = 5;
        const uint spiGetForegroundLockTimeout = 0x2000;
        const uint spiSetForegroundLockTimeout = 0x2001;
        const int maxActivationAttempts = 5;
        const int activationRetryDelayMs = 40;

        // A minimized window cannot receive focus until it is restored.
        if (NativeMethods.IsIconic(handle))
        {
            NativeMethods.ShowWindow(handle, swRestore);
        }

        // SetForegroundWindow returns true even when Windows merely flashes the
        // taskbar instead of activating, so its return value is never trusted:
        // GetForegroundWindow is the single source of truth. Trusting the bool
        // here previously skipped the AttachThreadInput fallback below.
        NativeMethods.SetForegroundWindow(handle);
        if (NativeMethods.GetForegroundWindow() == handle)
        {
            return true;
        }

        // Windows 11 reverts foreground changes initiated by a background
        // process. Making the activation stick requires both clearing the
        // foreground lock timeout and attaching our input queue to the
        // outgoing foreground thread and the target thread.
        uint originalLockTimeout = 0;
        NativeMethods.SystemParametersInfoGet(spiGetForegroundLockTimeout, 0, ref originalLockTimeout, 0);
        NativeMethods.SystemParametersInfoSet(spiSetForegroundLockTimeout, 0, nint.Zero, 0);

        var currentThread = NativeMethods.GetCurrentThreadId();
        var foreground = NativeMethods.GetForegroundWindow();
        var foregroundThread = foreground == IntPtr.Zero
            ? 0u
            : NativeMethods.GetWindowThreadProcessId(foreground, out _);
        var targetThread = NativeMethods.GetWindowThreadProcessId(handle, out _);

        var attachedForeground = foregroundThread != 0
            && foregroundThread != currentThread
            && NativeMethods.AttachThreadInput(currentThread, foregroundThread, true);
        var attachedTarget = targetThread != currentThread
            && targetThread != foregroundThread
            && NativeMethods.AttachThreadInput(currentThread, targetThread, true);

        try
        {
            // A few attempts: the activation occasionally needs the window
            // manager a moment to settle before it takes.
            for (var attempt = 0; attempt < maxActivationAttempts; attempt++)
            {
                NativeMethods.BringWindowToTop(handle);
                NativeMethods.ShowWindow(handle, swShow);
                NativeMethods.SetForegroundWindow(handle);

                if (NativeMethods.GetForegroundWindow() == handle)
                {
                    return true;
                }

                System.Threading.Thread.Sleep(activationRetryDelayMs);
            }

            return NativeMethods.GetForegroundWindow() == handle;
        }
        finally
        {
            if (attachedTarget)
            {
                NativeMethods.AttachThreadInput(currentThread, targetThread, false);
            }

            if (attachedForeground)
            {
                NativeMethods.AttachThreadInput(currentThread, foregroundThread, false);
            }

            NativeMethods.SystemParametersInfoSet(spiSetForegroundLockTimeout, 0, (nint)originalLockTimeout, 0);
        }
    }

    public WindowActionResult RaiseWindowForCapture(long windowHandle)
    {
        var handle = new IntPtr(windowHandle);
        var completed = RaiseWithoutActivating(handle);
        return new WindowActionResult(windowHandle, "raise_for_capture", completed, GetWindowBounds(handle), null);
    }

    // Brings a window to the top of the Z order so a screen capture no longer
    // photographs it behind whatever covers it, WITHOUT activating it or moving
    // the foreground/keyboard focus. This is the deliberate opposite of
    // ForceForeground: the user keeps typing into whatever they were using while
    // the target is merely re-stacked for the grab.
    private static bool RaiseWithoutActivating(IntPtr handle)
    {
        const int swShowNoActivate = 4;
        const uint swpNoSize = 0x0001;
        const uint swpNoMove = 0x0002;
        const uint swpNoActivate = 0x0010;
        const uint swpShowWindow = 0x0040;

        // A minimized window has nothing on screen to photograph. Restore it to
        // its previous size/position, but with SW_SHOWNOACTIVATE so it does not
        // steal activation the way the SW_RESTORE in ForceForeground would.
        if (NativeMethods.IsIconic(handle))
        {
            NativeMethods.ShowWindow(handle, swShowNoActivate);
        }

        // HWND_TOP (IntPtr.Zero) with SWP_NOACTIVATE lifts the window to the top
        // of the Z order while leaving the foreground window exactly where it is,
        // so keyboard focus never moves to the captured target.
        return NativeMethods.SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0,
            swpNoMove | swpNoSize | swpNoActivate | swpShowWindow);
    }

    public WindowActionResult MoveResizeWindow(long windowHandle, int? x, int? y, int? width, int? height)
    {
        var handle = new IntPtr(windowHandle);
        var current = GetWindowBounds(handle);
        var targetX = x ?? current.X;
        var targetY = y ?? current.Y;
        var targetWidth = width ?? current.Width;
        var targetHeight = height ?? current.Height;

        if (targetWidth <= 0 || targetHeight <= 0)
        {
            throw new ArgumentException("Window width and height must be greater than zero.");
        }

        var completed = NativeMethods.MoveWindow(handle, targetX, targetY, targetWidth, targetHeight, repaint: true);
        return new WindowActionResult(windowHandle, "move_resize", completed, new RectBounds(targetX, targetY, targetWidth, targetHeight), null);
    }

    public WindowActionResult SetWindowState(long windowHandle, string state)
    {
        var showCommand = state.ToLowerInvariant() switch
        {
            "hide" => 0,
            "normal" or "restore" => 9,
            "minimize" => 6,
            "maximize" => 3,
            _ => throw new ArgumentException($"Unsupported window state: {state}")
        };

        var completed = NativeMethods.ShowWindow(new IntPtr(windowHandle), showCommand);
        return new WindowActionResult(windowHandle, "set_state", completed, null, state);
    }

    public async Task<WindowDetails> WaitForWindowAsync(string? titleContains, string? className, string? processName, int? pid, int timeoutMs, CancellationToken cancellationToken, bool processNameExact = false)
    {
        var timeout = timeoutMs <= 0 ? 30000 : timeoutMs;
        var deadline = DateTimeOffset.UtcNow.AddMilliseconds(timeout);

        while (DateTimeOffset.UtcNow < deadline)
        {
            var match = FindWindows(titleContains, className, processName, pid, visibleOnly: false, processNameExact).FirstOrDefault();
            if (match is not null)
            {
                return match;
            }

            await Task.Delay(250, cancellationToken);
        }

        throw new TimeoutException("Timed out waiting for window.");
    }

    public IReadOnlyList<WindowDetails> EnumerateChildWindows(long windowHandle)
    {
        var windows = new List<WindowDetails>();
        NativeMethods.EnumChildWindows(new IntPtr(windowHandle), (hwnd, _) =>
        {
            NativeMethods.GetWindowThreadProcessId(hwnd, out var processId);
            windows.Add(new WindowDetails(
                hwnd,
                GetWindowText(hwnd),
                GetClassName(hwnd),
                (int)processId,
                GetProcessName((int)processId),
                NativeMethods.IsWindowVisible(hwnd),
                NativeMethods.IsIconic(hwnd),
                GetWindowBounds(hwnd)));

            return true;
        }, nint.Zero);

        return windows;
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

    private static bool Matches(WindowDetails window, string? titleContains, string? className, string? processName, int? pid, bool processNameExact)
    {
        if (!string.IsNullOrWhiteSpace(titleContains) && !window.Title.Contains(titleContains, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(className) && !string.Equals(window.ClassName, className, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(processName))
        {
            var processMatches = processNameExact
                ? string.Equals(window.ProcessName, processName, StringComparison.OrdinalIgnoreCase)
                : window.ProcessName.Contains(processName, StringComparison.OrdinalIgnoreCase);
            if (!processMatches)
            {
                return false;
            }
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
