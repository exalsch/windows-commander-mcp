using System.Windows.Forms;
using WindowsCommander.Core.Models;
using WindowsCommander.Core.Services;

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
