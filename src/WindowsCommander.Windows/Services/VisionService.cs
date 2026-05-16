using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
using WindowsCommander.Core.Models;
using WindowsCommander.Core.Services;
using WindowsCommander.Windows.Native;

namespace WindowsCommander.Windows.Services;

public sealed class VisionService : IVisionService
{
    // Caps the longest side of a returned capture so payloads stay small enough
    // to be usable; an explicit max_dimension argument can override this.
    private const int DefaultMaxDimension = 1400;

    public ScreenCaptureResult CaptureScreen(string target, long? windowHandle, int? maxDimension)
    {
        var region = ResolveCaptureRegion(target, windowHandle);
        return CaptureRegion(region, null, maxDimension);
    }

    public ScreenCaptureResult CaptureScreenRegion(int x, int y, int width, int height, string? monitorId, int? maxDimension)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentException("Capture width and height must be greater than zero.");
        }

        return CaptureRegion(new RectBounds(x, y, width, height), monitorId, maxDimension);
    }

    public OcrResult OcrScreen(string target, long? windowHandle, RectBounds? region)
    {
        var resolvedRegion = region ?? ResolveCaptureRegion(target, windowHandle);
        var blocks = WindowService.EnumerateWindows(visibleOnly: true)
            .Where(window => Intersects(window.BoundingRect, resolvedRegion) && !string.IsNullOrWhiteSpace(window.Title))
            .Select(window => new OcrTextBlock(window.Title, 0.70, window.BoundingRect))
            .ToArray();

        return new OcrResult(blocks, string.Join(Environment.NewLine, blocks.Select(block => block.Text)), resolvedRegion, DateTimeOffset.UtcNow);
    }

    public VisualDetectionResult DetectVisualElements(string target, long? windowHandle, RectBounds? region, IReadOnlyList<string>? elementTypes)
    {
        var resolvedRegion = region ?? ResolveCaptureRegion(target, windowHandle);
        var requestedTypes = elementTypes is null || elementTypes.Count == 0
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "window" }
            : new HashSet<string>(elementTypes, StringComparer.OrdinalIgnoreCase);

        var candidates = WindowService.EnumerateWindows(visibleOnly: true)
            .Where(window => requestedTypes.Contains("window") && Intersects(window.BoundingRect, resolvedRegion))
            .Select(window => new VisualElementCandidate("window", window.Title, 0.80, window.BoundingRect))
            .ToArray();

        return new VisualDetectionResult(candidates, resolvedRegion, DateTimeOffset.UtcNow);
    }

    private static ScreenCaptureResult CaptureRegion(RectBounds region, string? monitorId, int? maxDimension)
    {
        using var bitmap = new System.Drawing.Bitmap(region.Width, region.Height);
        using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(region.X, region.Y, 0, 0, new System.Drawing.Size(region.Width, region.Height));
        }

        var cap = maxDimension is > 0 ? maxDimension.Value : DefaultMaxDimension;
        var longestSide = Math.Max(region.Width, region.Height);

        using var stream = new MemoryStream();
        if (longestSide > cap)
        {
            var scale = (double)cap / longestSide;
            var scaledWidth = Math.Max(1, (int)Math.Round(region.Width * scale));
            var scaledHeight = Math.Max(1, (int)Math.Round(region.Height * scale));
            using var scaled = new System.Drawing.Bitmap(bitmap, scaledWidth, scaledHeight);
            scaled.Save(stream, ImageFormat.Png);
        }
        else
        {
            bitmap.Save(stream, ImageFormat.Png);
        }

        return new ScreenCaptureResult(Convert.ToBase64String(stream.ToArray()), region, monitorId, DateTimeOffset.UtcNow);
    }

    private static RectBounds ResolveCaptureRegion(string target, long? windowHandle)
    {
        if (target.Equals("full_screen", StringComparison.OrdinalIgnoreCase))
        {
            var bounds = SystemInformation.VirtualScreen;
            return new RectBounds(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        }

        if (target.Equals("active_window", StringComparison.OrdinalIgnoreCase))
        {
            var window = WindowService.EnumerateWindows(visibleOnly: true).FirstOrDefault(window => !string.IsNullOrWhiteSpace(window.Title));
            return window?.BoundingRect ?? ResolveCaptureRegion("full_screen", null);
        }

        if (windowHandle is null && long.TryParse(target, out var parsedHandle))
        {
            windowHandle = parsedHandle;
        }

        if (windowHandle is not null)
        {
            if (!NativeMethods.GetWindowRect(new IntPtr(windowHandle.Value), out var rect))
            {
                throw new ArgumentException($"Window handle was not found: {windowHandle}");
            }

            return new RectBounds(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
        }

        throw new ArgumentException($"Unsupported capture target: {target}");
    }

    private static bool Intersects(RectBounds left, RectBounds right)
    {
        return left.X < right.X + right.Width
            && left.X + left.Width > right.X
            && left.Y < right.Y + right.Height
            && left.Y + left.Height > right.Y;
    }
}
