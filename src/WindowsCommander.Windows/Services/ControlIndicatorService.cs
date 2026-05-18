using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using WindowsCommander.Core.Models;
using WindowsCommander.Core.Services;
using Forms = System.Windows.Forms;

namespace WindowsCommander.Windows.Services;

// The glow has two phases: a bright Active pulse while a computer-use tool
// runs, and a faint Idle border that lingers afterwards so the user can still
// see a session is connected.
internal enum ActivityPhase
{
    Active,
    Idle
}

// One entry in the activity-label queue shown along the glow's top edge.
internal readonly record struct ActivityLabel(string Text, bool Elevated);

public sealed class ControlIndicatorService : IControlIndicatorService
{
    // How long the bright activity glow lingers after the last computer-use
    // tool ran before settling to the faint idle border.
    private static readonly TimeSpan ActivityHold = TimeSpan.FromMilliseconds(2000);

    // How long the faint idle border lingers with no activity before the glow
    // is hidden entirely.
    private static readonly TimeSpan SessionIdleHold = TimeSpan.FromSeconds(30);

    private readonly object syncRoot = new();

    // The activity-label queue: recent actions shown as horizontal chips so a
    // burst of calls reads as a short history. New chips append on the right,
    // the oldest drop off at the cap. Because each chip is a separate box, a
    // screenshot taken mid-render shows fewer chips rather than a stale label
    // misattributed to the current action.
    private const int MaxActivityLabels = 5;
    private readonly Queue<ActivityLabel> activityLabels = new();

    // One overlay window per glowed rectangle. Accessed only on the overlay
    // dispatcher thread, so it needs no locking of its own.
    private readonly List<ControlIndicatorOverlayWindow> overlayWindows = new();

    private ControlIndicatorConfig config = new(VisualEnabled: true, AudioEnabled: true, BorderColor: "Red", BorderThickness: 4, AudioFrequencyHz: 880, AudioDurationMs: 150);
    private ControlIndicatorStatus status;
    private Thread? overlayThread;
    private Dispatcher? overlayDispatcher;
    private System.Threading.Timer? activeToIdleTimer;
    private System.Threading.Timer? idleToHiddenTimer;

    public ControlIndicatorService()
    {
        status = new ControlIndicatorStatus(IsVisible: false, null, null, config);
    }

    public ControlIndicatorStatus ShowControlIndicator(string message, RectBounds? bounds)
    {
        if (config.AudioEnabled)
        {
            SystemSounds.Asterisk.Play();
        }

        status = new ControlIndicatorStatus(IsVisible: config.VisualEnabled, message, bounds, config);
        if (config.VisualEnabled)
        {
            ShowOverlay(new[] { new ActivityLabel(message, false) }, RectanglesFor(bounds), config, ActivityPhase.Active);
        }
        else
        {
            HideOverlay();
        }

        return status;
    }

    public ControlIndicatorStatus HideControlIndicator()
    {
        HideOverlay();
        status = new ControlIndicatorStatus(IsVisible: false, null, null, config);
        return status;
    }

    public ControlIndicatorStatus ConfigureControlIndicators(ControlIndicatorConfig config)
    {
        this.config = config;
        status = status with { Config = config };
        if (status.IsVisible && config.VisualEnabled)
        {
            ShowOverlay(new[] { new ActivityLabel(status.Message ?? string.Empty, false) }, RectanglesFor(status.Bounds), config, ActivityPhase.Active);
        }
        else if (!config.VisualEnabled)
        {
            HideOverlay();
            status = status with { IsVisible = false };
        }

        return status;
    }

    public ControlIndicatorStatus GetControlIndicatorStatus()
    {
        return status;
    }

    public void SignalActivity(string message, bool elevated)
    {
        // The activity glow must never disrupt the tool call that triggered it.
        try
        {
            if (!config.VisualEnabled)
            {
                return;
            }

            // Append the action to the label queue, capped at MaxActivityLabels.
            ActivityLabel[] queue;
            lock (syncRoot)
            {
                activityLabels.Enqueue(new ActivityLabel($"{(elevated ? "⚠" : "⚡")} {message}", elevated));
                while (activityLabels.Count > MaxActivityLabels)
                {
                    activityLabels.Dequeue();
                }

                queue = activityLabels.ToArray();
            }

            // High-risk actions glow in a warning colour: the cue then says
            // "automation is doing something risky", not merely "active".
            var overlayConfig = elevated ? config with { BorderColor = "Orange" } : config;
            ShowOverlay(queue, RectanglesFor(null), overlayConfig, ActivityPhase.Active);

            lock (syncRoot)
            {
                // Active glow -> faint idle border after ActivityHold; faint
                // idle border -> hidden after a further SessionIdleHold. Both
                // are deferred while a manual indicator is shown.
                activeToIdleTimer ??= new System.Threading.Timer(_ =>
                {
                    if (!status.IsVisible)
                    {
                        // The action history is stale once activity stops:
                        // clear it so the next burst starts with a fresh queue.
                        lock (syncRoot)
                        {
                            activityLabels.Clear();
                        }

                        SetOverlayPhase(ActivityPhase.Idle);
                    }
                });
                idleToHiddenTimer ??= new System.Threading.Timer(_ =>
                {
                    if (!status.IsVisible)
                    {
                        HideOverlay();
                    }
                });
                activeToIdleTimer.Change(ActivityHold, Timeout.InfiniteTimeSpan);
                idleToHiddenTimer.Change(SessionIdleHold, Timeout.InfiniteTimeSpan);
            }
        }
        catch
        {
            // Ignore: the indicator is a courtesy, not part of the contract.
        }
    }

    public ConfirmationResult RequestUserConfirmation(string title, string message, string riskLevel, int? timeoutMs)
    {
        Forms.DialogResult result = Forms.DialogResult.None;
        var thread = new Thread(() =>
        {
            result = Forms.MessageBox.Show(message, title, Forms.MessageBoxButtons.YesNo, Forms.MessageBoxIcon.Warning, Forms.MessageBoxDefaultButton.Button2);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        var completed = thread.Join(Math.Clamp(timeoutMs ?? 30000, 1000, 300000));
        var decision = completed && result == Forms.DialogResult.Yes ? "approved" : completed ? "denied" : "timeout";
        return new ConfirmationResult(title, message, riskLevel, decision, DateTimeOffset.UtcNow);
    }

    // Resolves which rectangles to glow. An explicit bounds is shown as a single
    // rectangle; otherwise every screen is framed individually so each gets a
    // correctly sized glow regardless of its own resolution or position — a
    // single virtual-desktop rectangle would only fit the tallest monitor.
    private static IReadOnlyList<RectBounds> RectanglesFor(RectBounds? explicitBounds)
    {
        if (explicitBounds is not null)
        {
            return new[] { explicitBounds };
        }

        return Forms.Screen.AllScreens
            .Select(screen => new RectBounds(screen.Bounds.X, screen.Bounds.Y, screen.Bounds.Width, screen.Bounds.Height))
            .ToArray();
    }

    private void ShowOverlay(IReadOnlyList<ActivityLabel> messages, IReadOnlyList<RectBounds> rectangles, ControlIndicatorConfig overlayConfig, ActivityPhase phase)
    {
        EnsureOverlayThread();
        overlayDispatcher?.Invoke(() =>
        {
            while (overlayWindows.Count < rectangles.Count)
            {
                overlayWindows.Add(new ControlIndicatorOverlayWindow());
            }

            for (var index = 0; index < rectangles.Count; index++)
            {
                overlayWindows[index].ShowOverlay(messages, rectangles[index], overlayConfig, phase);
            }

            // Hide any windows left over from a previous, larger set.
            for (var index = rectangles.Count; index < overlayWindows.Count; index++)
            {
                overlayWindows[index].Hide();
            }
        });
    }

    // Dims the already-visible glow to its faint idle state without otherwise
    // disturbing it (the session is still connected, just not mid-action).
    private void SetOverlayPhase(ActivityPhase phase)
    {
        overlayDispatcher?.Invoke(() =>
        {
            foreach (var window in overlayWindows)
            {
                if (window.IsVisible)
                {
                    window.SetPhase(phase);
                }
            }
        });
    }

    private void HideOverlay()
    {
        overlayDispatcher?.Invoke(() =>
        {
            foreach (var window in overlayWindows)
            {
                window.Hide();
            }
        });
    }

    private void EnsureOverlayThread()
    {
        lock (syncRoot)
        {
            if (overlayDispatcher is not null && overlayThread?.IsAlive == true)
            {
                return;
            }

            var ready = new ManualResetEventSlim();
            overlayThread = new Thread(() =>
            {
                overlayDispatcher = Dispatcher.CurrentDispatcher;
                ready.Set();
                Dispatcher.Run();
            });
            overlayThread.SetApartmentState(ApartmentState.STA);
            overlayThread.IsBackground = true;
            overlayThread.Start();
            ready.Wait(TimeSpan.FromSeconds(5));
        }
    }

    private sealed class ControlIndicatorOverlayWindow : Window
    {
        private readonly Border border;
        private readonly StackPanel labelPanel;
        private readonly DropShadowEffect glow;

        public ControlIndicatorOverlayWindow()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = System.Windows.Media.Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            IsHitTestVisible = false;
            Focusable = false;
            // Critical: the overlay must never take the foreground, or it would
            // steal focus from the very window the automation is driving.
            ShowActivated = false;

            // A horizontal queue of action chips, anchored to the top-left of
            // the framed screen.
            labelPanel = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                VerticalAlignment = System.Windows.VerticalAlignment.Top,
                Margin = new Thickness(6)
            };

            // ShadowDepth 0 turns the drop shadow into a symmetric glow that
            // haloes the border stroke; the inward half is what the user sees
            // along each screen edge.
            glow = new DropShadowEffect
            {
                ShadowDepth = 0,
                BlurRadius = 22,
                Opacity = 1.0,
                Color = System.Windows.Media.Colors.Red
            };

            border = new Border
            {
                Background = System.Windows.Media.Brushes.Transparent,
                Child = labelPanel,
                Effect = glow
            };

            Content = border;

            // A slow breathing pulse reads as "active" rather than a static
            // frame. It runs continuously; window visibility gates whether it
            // is seen, so there is nothing to start or stop per activation.
            border.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation
            {
                From = 1.0,
                To = 0.45,
                Duration = TimeSpan.FromMilliseconds(750),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase()
            });
        }

        // Window opacity for the faint persistent idle border. Low enough to
        // read as "session connected" without competing with the active glow.
        private const double IdleOpacity = 0.3;

        public void ShowOverlay(IReadOnlyList<ActivityLabel> messages, RectBounds bounds, ControlIndicatorConfig config, ActivityPhase phase)
        {
            Left = bounds.X;
            Top = bounds.Y;
            Width = Math.Max(1, bounds.Width);
            Height = Math.Max(1, bounds.Height);

            var brush = ParseBrush(config.BorderColor);
            border.BorderBrush = brush;
            border.BorderThickness = new Thickness(Math.Max(1, config.BorderThickness));
            glow.Color = (brush as SolidColorBrush)?.Color ?? System.Windows.Media.Colors.Red;
            SetMessages(messages);
            ApplyPhase(phase);

            if (!IsVisible)
            {
                Show();
            }
        }

        // Switches an already-visible overlay between the bright active glow
        // and the faint idle border without re-showing it.
        public void SetPhase(ActivityPhase phase)
        {
            ApplyPhase(phase);
        }

        // Rebuilds the chip queue. Each action is its own box, so a partial
        // render shows fewer chips rather than a label misattributed to the
        // wrong action.
        private void SetMessages(IReadOnlyList<ActivityLabel> messages)
        {
            labelPanel.Children.Clear();
            for (var index = 0; index < messages.Count; index++)
            {
                labelPanel.Children.Add(BuildChip(messages[index], newest: index == messages.Count - 1));
            }
        }

        private static Border BuildChip(ActivityLabel message, bool newest)
        {
            // The newest action stands out; older chips recede into history.
            var alpha = (byte)(newest ? 215 : 140);
            var background = message.Elevated
                ? new SolidColorBrush(System.Windows.Media.Color.FromArgb(alpha, 150, 70, 0))
                : new SolidColorBrush(System.Windows.Media.Color.FromArgb(alpha, 0, 0, 0));

            return new Border
            {
                Background = background,
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(7, 3, 7, 3),
                Margin = new Thickness(0, 0, 4, 0),
                Child = new TextBlock
                {
                    Text = message.Text,
                    Foreground = System.Windows.Media.Brushes.White,
                    FontSize = 12,
                    FontWeight = newest ? FontWeights.SemiBold : FontWeights.Normal,
                    Opacity = newest ? 1.0 : 0.65
                }
            };
        }

        private void ApplyPhase(ActivityPhase phase)
        {
            Opacity = phase == ActivityPhase.Active ? 1.0 : IdleOpacity;
            // The label chips are noise once the action is over; the idle
            // border alone carries the "session still connected" cue.
            labelPanel.Visibility = phase == ActivityPhase.Active
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private static System.Windows.Media.Brush ParseBrush(string color)
        {
            try
            {
                return new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));
            }
            catch (FormatException)
            {
                return System.Windows.Media.Brushes.Red;
            }
        }
    }
}
