using System.IO;
using System.Media;

namespace WindowsCommander.McpServer.Mcp;

/// <summary>
/// Plays a local notification sound when the server performs computer-use
/// actions (mouse, keyboard, window, UI, or screen-capture tools), so the user
/// has an audible cue that automation is driving their desktop.
/// </summary>
internal sealed class ComputerUseNotifier
{
    // A burst of computer-use calls (e.g. a long input_sequence) collapses to a
    // single chime rather than overlapping playback.
    private static readonly TimeSpan Debounce = TimeSpan.FromSeconds(2);

    private readonly SoundPlayer? player;
    private readonly object gate = new();
    private DateTimeOffset lastPlayed = DateTimeOffset.MinValue;

    public ComputerUseNotifier()
    {
        var soundPath = Path.Combine(AppContext.BaseDirectory, "Assets", "computer-use-notification.wav");
        if (File.Exists(soundPath))
        {
            // The instance is kept alive for the process lifetime; asynchronous
            // playback relies on the SoundPlayer not being collected mid-play.
            player = new SoundPlayer(soundPath);
        }
    }

    public void Notify()
    {
        if (player is null)
        {
            return;
        }

        lock (gate)
        {
            var now = DateTimeOffset.UtcNow;
            if (now - lastPlayed < Debounce)
            {
                return;
            }

            lastPlayed = now;
        }

        try
        {
            player.Play();
        }
        catch
        {
            // A notification sound must never disrupt tool execution.
        }
    }
}
