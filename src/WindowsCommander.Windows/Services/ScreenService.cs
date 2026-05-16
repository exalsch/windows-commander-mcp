using System.Windows.Forms;
using System.Text;
using WindowsCommander.Core.Models;
using WindowsCommander.Core.Services;
using WindowsCommander.Windows.Native;

namespace WindowsCommander.Windows.Services;

public sealed class ScreenService : IScreenService
{
    public IReadOnlyList<ScreenDetails> GetScreenDetails()
    {
        return Screen.AllScreens
            .Select((screen, index) => ToDetails(screen, index))
            .ToArray();
    }

    public ScreenAtPoint GetScreenAtPoint(int x, int y)
    {
        var point = new System.Drawing.Point(x, y);
        var screen = Screen.FromPoint(point);
        var index = Array.IndexOf(Screen.AllScreens, screen);
        var details = ToDetails(screen, index < 0 ? 0 : index);
        var workingArea = screen.WorkingArea;

        return new ScreenAtPoint(
            details,
            x - screen.Bounds.X,
            y - screen.Bounds.Y,
            workingArea.Contains(point));
    }

    public IReadOnlyList<DisplayMetric> GetDisplayMetrics()
    {
        return Screen.AllScreens
            .Select((screen, index) =>
            {
                var bounds = screen.Bounds;
                return new DisplayMetric(
                    $"screen-{index + 1}",
                    screen.DeviceName,
                    $"{bounds.Width}x{bounds.Height}",
                    new RectBounds(bounds.X, bounds.Y, bounds.Width, bounds.Height),
                    0,
                    1.0);
            })
            .ToArray();
    }

    public WindowScreenInfo GetWindowScreenInfo(long windowHandle)
    {
        var handle = new IntPtr(windowHandle);
        if (!NativeMethods.GetWindowRect(handle, out var rect))
        {
            throw new ArgumentException($"Window handle was not found: {windowHandle}");
        }

        var titleBuilder = new StringBuilder(512);
        _ = NativeMethods.GetWindowText(handle, titleBuilder, titleBuilder.Capacity);
        var title = titleBuilder.ToString();
        var windowBounds = new RectBounds(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
        var screen = Screen.FromRectangle(new System.Drawing.Rectangle(windowBounds.X, windowBounds.Y, windowBounds.Width, windowBounds.Height));
        var screenIndex = Array.IndexOf(Screen.AllScreens, screen);
        var screenDetails = ToDetails(screen, screenIndex < 0 ? 0 : screenIndex);
        var intersection = System.Drawing.Rectangle.Intersect(
            new System.Drawing.Rectangle(windowBounds.X, windowBounds.Y, windowBounds.Width, windowBounds.Height),
            screen.Bounds);
        var windowArea = Math.Max(1, windowBounds.Width * windowBounds.Height);
        var visibleArea = Math.Max(0, intersection.Width * intersection.Height);

        return new WindowScreenInfo(
            windowHandle,
            title,
            windowBounds,
            screenDetails,
            visibleArea == windowArea,
            visibleArea / (double)windowArea);
    }

    public NotificationResult ShowNotification(string title, string message, int? timeoutMs)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("Notification title must not be empty.", nameof(title));
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("Notification message must not be empty.", nameof(message));
        }

        using var notifyIcon = new NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Information,
            Visible = true,
            BalloonTipTitle = title,
            BalloonTipText = message
        };
        notifyIcon.ShowBalloonTip(Math.Clamp(timeoutMs ?? 5000, 1000, 30000));

        return new NotificationResult(title, message, DateTimeOffset.UtcNow, Delivered: true);
    }

    private static ScreenDetails ToDetails(Screen screen, int index)
    {
        var bounds = screen.Bounds;
        var workingArea = screen.WorkingArea;

        return new ScreenDetails(
            $"screen-{index + 1}",
            screen.DeviceName,
            screen.DeviceName,
            screen.Primary,
            new RectBounds(bounds.X, bounds.Y, bounds.Width, bounds.Height),
            new RectBounds(workingArea.X, workingArea.Y, workingArea.Width, workingArea.Height),
            1.0,
            bounds.Width >= bounds.Height ? "Landscape" : "Portrait",
            $"{bounds.Width}x{bounds.Height}",
            0,
            0,
            string.Empty,
            true);
    }
}
