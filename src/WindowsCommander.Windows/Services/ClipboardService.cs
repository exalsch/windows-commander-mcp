using System.Windows.Forms;
using WindowsCommander.Core.Services;

namespace WindowsCommander.Windows.Services;

public sealed class ClipboardService : IClipboardService
{
    public object? Access(string action, string? content, string? format)
    {
        if (!string.Equals(format ?? "text", "text", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Only text clipboard format is implemented in this slice.");
        }

        return RunOnStaThread(() => action.ToLowerInvariant() switch
        {
            "read" => Clipboard.ContainsText() ? Clipboard.GetText() : string.Empty,
            "write" => WriteText(content),
            "clear" => ClearClipboard(),
            _ => throw new ArgumentException($"Unsupported clipboard action: {action}")
        });
    }

    private static object? WriteText(string? content)
    {
        Clipboard.SetText(content ?? string.Empty);
        return new { written = true };
    }

    private static object? ClearClipboard()
    {
        Clipboard.Clear();
        return new { cleared = true };
    }

    private static T RunOnStaThread<T>(Func<T> action)
    {
        T? result = default;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                result = action();
            }
            catch (Exception exception)
            {
                error = exception;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (error is not null)
        {
            throw error;
        }

        return result!;
    }
}
