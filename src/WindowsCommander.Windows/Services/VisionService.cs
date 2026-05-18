using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using WindowsCommander.Core.Models;
using WindowsCommander.Core.Services;
using WindowsCommander.Windows.Native;
using WinRtOcr = Windows.Media.Ocr;

namespace WindowsCommander.Windows.Services;

public sealed class VisionService : IVisionService
{
    // Caps the longest side of a returned capture so payloads stay small enough
    // to be usable; an explicit max_dimension argument can override this.
    private const int DefaultMaxDimension = 1400;

    // The Windows on-device OCR engine, created lazily from the user's
    // installed languages and reused across calls.
    private WinRtOcr.OcrEngine? ocrEngine;

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

    public async Task<OcrResult> OcrScreenAsync(string target, long? windowHandle, RectBounds? region)
    {
        var resolvedRegion = region ?? ResolveCaptureRegion(target, windowHandle);
        if (resolvedRegion.Width <= 0 || resolvedRegion.Height <= 0)
        {
            throw new ArgumentException("OCR region width and height must be greater than zero.");
        }

        var engine = GetOcrEngine();

        // Capture the region's pixels.
        using var bitmap = new System.Drawing.Bitmap(resolvedRegion.Width, resolvedRegion.Height);
        using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(resolvedRegion.X, resolvedRegion.Y, 0, 0, new System.Drawing.Size(resolvedRegion.Width, resolvedRegion.Height));
        }

        // OcrEngine rejects images longer than MaxImageDimension on a side.
        // Scale down to fit and divide the result rects back up by the factor.
        var maxDimension = (int)WinRtOcr.OcrEngine.MaxImageDimension;
        var longestSide = Math.Max(bitmap.Width, bitmap.Height);
        var scale = longestSide > maxDimension ? (double)maxDimension / longestSide : 1.0;

        var softwareBitmap = scale < 1.0
            ? await ToSoftwareBitmapAsync(bitmap, scale)
            : await ToSoftwareBitmapAsync(bitmap, 1.0);

        try
        {
            var recognized = await engine.RecognizeAsync(softwareBitmap);
            var blocks = recognized.Lines
                .Select(line => ToBlock(line, resolvedRegion, scale))
                .Where(block => block is not null)
                .Select(block => block!)
                .ToArray();

            return new OcrResult(blocks, recognized.Text ?? string.Empty, resolvedRegion, DateTimeOffset.UtcNow);
        }
        finally
        {
            softwareBitmap.Dispose();
        }
    }

    private WinRtOcr.OcrEngine GetOcrEngine()
    {
        return ocrEngine ??= WinRtOcr.OcrEngine.TryCreateFromUserProfileLanguages()
            ?? throw new InvalidOperationException(
                "No OCR language pack is available. Add one via Settings > Time & language > Language & region.");
    }

    // Renders a GDI bitmap (optionally scaled) into the BGRA8 SoftwareBitmap
    // the OCR engine expects.
    private static async Task<SoftwareBitmap> ToSoftwareBitmapAsync(System.Drawing.Bitmap bitmap, double scale)
    {
        byte[] bytes;
        using (var memory = new MemoryStream())
        {
            if (scale < 1.0)
            {
                var width = Math.Max(1, (int)Math.Round(bitmap.Width * scale));
                var height = Math.Max(1, (int)Math.Round(bitmap.Height * scale));
                using var scaled = new System.Drawing.Bitmap(bitmap, width, height);
                scaled.Save(memory, ImageFormat.Bmp);
            }
            else
            {
                bitmap.Save(memory, ImageFormat.Bmp);
            }

            bytes = memory.ToArray();
        }

        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream))
        {
            writer.WriteBytes(bytes);
            await writer.StoreAsync();
            await writer.FlushAsync();
            writer.DetachStream();
        }

        stream.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(stream);
        var decoded = await decoder.GetSoftwareBitmapAsync();
        if (decoded.BitmapPixelFormat == BitmapPixelFormat.Bgra8 && decoded.BitmapAlphaMode == BitmapAlphaMode.Premultiplied)
        {
            return decoded;
        }

        var converted = SoftwareBitmap.Convert(decoded, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        decoded.Dispose();
        return converted;
    }

    // Collapses an OCR line into a text block, mapping its bitmap-relative
    // bounds back into virtual-screen coordinates.
    private static OcrTextBlock? ToBlock(WinRtOcr.OcrLine line, RectBounds region, double scale)
    {
        if (line.Words.Count == 0)
        {
            return null;
        }

        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var word in line.Words)
        {
            var rect = word.BoundingRect;
            minX = Math.Min(minX, rect.X);
            minY = Math.Min(minY, rect.Y);
            maxX = Math.Max(maxX, rect.X + rect.Width);
            maxY = Math.Max(maxY, rect.Y + rect.Height);
        }

        // Rects are relative to the (possibly scaled) captured bitmap: undo the
        // scale, then offset by the region origin to reach screen coordinates.
        var bounds = new RectBounds(
            region.X + (int)Math.Round(minX / scale),
            region.Y + (int)Math.Round(minY / scale),
            (int)Math.Round((maxX - minX) / scale),
            (int)Math.Round((maxY - minY) / scale));

        // Windows.Media.Ocr does not expose a per-line confidence score.
        return new OcrTextBlock(line.Text, 1.0, bounds);
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

        // Per-monitor capture options exist because "full_screen" grabs the
        // whole virtual screen — on multi-monitor setups that can be ~8000px
        // wide, and downscaling it to fit the payload cap renders all text
        // unreadable. Capturing a single monitor keeps the result legible.
        if (target.Equals("primary_screen", StringComparison.OrdinalIgnoreCase))
        {
            var primary = Screen.PrimaryScreen ?? throw new ArgumentException("No primary screen is available.");
            var bounds = primary.Bounds;
            return new RectBounds(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        }

        if (target.StartsWith("screen-", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(target["screen-".Length..], out var screenNumber))
        {
            var screens = Screen.AllScreens;
            if (screenNumber < 1 || screenNumber > screens.Length)
            {
                throw new ArgumentException($"Screen index out of range: '{target}'. Valid range is screen-1 to screen-{screens.Length}.");
            }

            var bounds = screens[screenNumber - 1].Bounds;
            return new RectBounds(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        }

        if (target.Equals("active_window", StringComparison.OrdinalIgnoreCase))
        {
            // The real foreground window — not just the first title-bearing
            // window in z-order. Falls back to full_screen if it cannot be
            // determined.
            var foreground = NativeMethods.GetForegroundWindow();
            if (foreground != IntPtr.Zero && NativeMethods.GetWindowRect(foreground, out var rect))
            {
                return new RectBounds(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
            }

            return ResolveCaptureRegion("full_screen", null);
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
