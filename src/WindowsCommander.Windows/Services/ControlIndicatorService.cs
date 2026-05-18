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

    // Each chip fades out this long after it was added, so the queue shows a
    // rolling window of just-happened actions rather than the whole session.
    private static readonly TimeSpan ChipLifetime = TimeSpan.FromSeconds(4);
    private readonly List<(ActivityLabel Label, DateTime AddedUtc)> activityLabels = new();

    // One overlay window per glowed rectangle. Accessed only on the overlay
    // dispatcher thread, so it needs no locking of its own.
    private readonly List<ControlIndicatorOverlayWindow> overlayWindows = new();

    private ControlIndicatorConfig config = new(VisualEnabled: true, AudioEnabled: true, BorderColor: "Red", BorderThickness: 4, AudioFrequencyHz: 880, AudioDurationMs: 150);
    private ControlIndicatorStatus status;
    private Thread? overlayThread;
    private Dispatcher? overlayDispatcher;
    private System.Threading.Timer? activeToIdleTimer;
    private System.Threading.Timer? idleToHiddenTimer;
    private System.Threading.Timer? chipExpiryTimer;

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

            // Append the action to the label queue, drop expired ones, cap it.
            ActivityLabel[] queue;
            lock (syncRoot)
            {
                activityLabels.Add((new ActivityLabel($"{(elevated ? "⚠" : "⚡")} {message}", elevated), DateTime.UtcNow));
                PruneExpiredChips();
                while (activityLabels.Count > MaxActivityLabels)
                {
                    activityLabels.RemoveAt(0);
                }

                queue = activityLabels.Select(entry => entry.Label).ToArray();
            }

            ScheduleChipExpiry();

            // High-risk actions glow in a warning colour: the cue then says
            // "automation is doing something risky", not merely "active".
            var overlayConfig = elevated ? config with { BorderColor = "Orange" } : config;
            ShowOverlay(queue, RectanglesFor(null), overlayConfig, ActivityPhase.Active);

            lock (syncRoot)
            {
                // Active glow -> faint idle border after ActivityHold (the chip
                // history stays visible); idle border -> hidden after a further
                // SessionIdleHold. Both are deferred while a manual indicator
                // is shown. Agent actions are usually seconds apart, so the
                // chips must persist across the gaps for the queue to build.
                activeToIdleTimer ??= new System.Threading.Timer(_ =>
                {
                    if (!status.IsVisible)
                    {
                        SetOverlayPhase(ActivityPhase.Idle);
                    }
                });
                idleToHiddenTimer ??= new System.Threading.Timer(_ =>
                {
                    if (!status.IsVisible)
                    {
                        lock (syncRoot)
                        {
                            activityLabels.Clear();
                        }

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

    // Drops chips older than ChipLifetime. Must be called holding syncRoot.
    private void PruneExpiredChips()
    {
        var cutoff = DateTime.UtcNow - ChipLifetime;
        activityLabels.RemoveAll(entry => entry.AddedUtc < cutoff);
    }

    // Arms the expiry timer to fire when the oldest chip is due to leave.
    private void ScheduleChipExpiry()
    {
        lock (syncRoot)
        {
            chipExpiryTimer ??= new System.Threading.Timer(_ => OnChipExpiry());
            if (activityLabels.Count == 0)
            {
                return;
            }

            var due = (activityLabels[0].AddedUtc + ChipLifetime) - DateTime.UtcNow;
            chipExpiryTimer.Change(due < TimeSpan.Zero ? TimeSpan.Zero : due, Timeout.InfiniteTimeSpan);
        }
    }

    // Fired when a chip's lifetime ends: prune, re-render the shrunk queue
    // (the overlay fades the dropped chips out), then arm for the next one.
    private void OnChipExpiry()
    {
        try
        {
            ActivityLabel[] queue;
            lock (syncRoot)
            {
                PruneExpiredChips();
                queue = activityLabels.Select(entry => entry.Label).ToArray();
            }

            overlayDispatcher?.Invoke(() =>
            {
                foreach (var window in overlayWindows)
                {
                    if (window.IsVisible)
                    {
                        window.RefreshChips(queue);
                    }
                }
            });

            ScheduleChipExpiry();
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
        // Glow-halo opacity at the three points of an action's life: the
        // instant a tool runs (flash peak), the calm hold between actions
        // (steady), and the faint "session still connected" border (idle).
        // Driven on the drop-shadow effect, not the border, so the chip row
        // stays fully readable throughout.
        private const double FlashGlow = 1.0;
        private const double SteadyGlow = 0.7;
        private const double IdleGlow = 0.32;
        private const double FlashBlur = 38;
        private const double SteadyBlur = 18;
        private const double IdleBlur = 11;

        private static readonly Duration FlashDecay = new(TimeSpan.FromMilliseconds(550));
        private static readonly Duration PhaseFade = new(TimeSpan.FromMilliseconds(450));
        private static readonly Duration ChipMotion = new(TimeSpan.FromMilliseconds(260));

        // How far a freshly-queued chip slides in from the right.
        private const double ChipSlide = 40;

        private readonly Border border;
        private readonly StackPanel labelPanel;
        private readonly DropShadowEffect glow;

        // Logical chip state, parallel lists. labelPanel.Children is a superset:
        // it also holds chips that are mid fade-out.
        private readonly List<ActivityLabel> chipModels = new();
        private readonly List<Border> chipElements = new();

        private double baseThickness = 4;

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
                BlurRadius = SteadyBlur,
                Opacity = SteadyGlow,
                Color = System.Windows.Media.Colors.Red
            };

            // The frame sits at its steady level; each action drives a one-shot
            // flash on the glow halo rather than an ambient breathing loop, so
            // it visibly reacts. The border itself stays at full opacity so the
            // chip row never dims.
            border = new Border
            {
                Background = System.Windows.Media.Brushes.Transparent,
                Child = labelPanel,
                Effect = glow
            };

            Content = border;
        }

        // Shows/refreshes the overlay. An Active phase fires a one-shot flash
        // pulse for the action; an Idle phase fades down to the faint border.
        public void ShowOverlay(IReadOnlyList<ActivityLabel> messages, RectBounds bounds, ControlIndicatorConfig config, ActivityPhase phase)
        {
            Left = bounds.X;
            Top = bounds.Y;
            Width = Math.Max(1, bounds.Width);
            Height = Math.Max(1, bounds.Height);

            var brush = ParseBrush(config.BorderColor);
            border.BorderBrush = brush;
            baseThickness = Math.Max(1, config.BorderThickness);
            glow.Color = (brush as SolidColorBrush)?.Color ?? System.Windows.Media.Colors.Red;

            if (!IsVisible)
            {
                // A hidden -> shown transition starts a fresh session: drop any
                // chips left over so the new queue animates in cleanly.
                labelPanel.Children.Clear();
                chipModels.Clear();
                chipElements.Clear();
                Show();
            }

            if (phase == ActivityPhase.Active)
            {
                UpdateChips(messages);
                Flash();
            }
            else
            {
                EnterIdle();
            }
        }

        // Switches an already-visible overlay between an action flash and the
        // faint idle border without re-showing it.
        public void SetPhase(ActivityPhase phase)
        {
            if (phase == ActivityPhase.Active)
            {
                Flash();
            }
            else
            {
                EnterIdle();
            }
        }

        // Re-renders the chip row (fading expired chips out) with no flash and
        // no phase change — used when a chip's lifetime ends between actions.
        public void RefreshChips(IReadOnlyList<ActivityLabel> messages)
        {
            UpdateChips(messages);
        }

        // A sharp bright spike that decays back to the steady glow, so every
        // action visibly "lands" instead of blending into an ambient pulse.
        // The glow halo carries the flash; the border stroke and chips do not
        // dim, so the chip history stays readable.
        private void Flash()
        {
            var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };

            glow.BeginAnimation(DropShadowEffect.OpacityProperty, new DoubleAnimation
            {
                From = FlashGlow,
                To = SteadyGlow,
                Duration = FlashDecay,
                EasingFunction = ease
            });
            glow.BeginAnimation(DropShadowEffect.BlurRadiusProperty, new DoubleAnimation
            {
                From = FlashBlur,
                To = SteadyBlur,
                Duration = FlashDecay,
                EasingFunction = ease
            });
            border.BeginAnimation(Border.BorderThicknessProperty, new ThicknessAnimation
            {
                From = new Thickness(baseThickness * 2.2),
                To = new Thickness(baseThickness),
                Duration = FlashDecay,
                EasingFunction = ease
            });
        }

        // Fades the glow down to the faint persistent idle level. The chip row
        // stays put — it is a rolling history of recent actions and only clears
        // when the session goes fully idle and the overlay hides.
        private void EnterIdle()
        {
            var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };

            glow.BeginAnimation(DropShadowEffect.OpacityProperty, new DoubleAnimation { To = IdleGlow, Duration = PhaseFade, EasingFunction = ease });
            glow.BeginAnimation(DropShadowEffect.BlurRadiusProperty, new DoubleAnimation { To = IdleBlur, Duration = PhaseFade, EasingFunction = ease });
            border.BeginAnimation(Border.BorderThicknessProperty, new ThicknessAnimation { To = new Thickness(baseThickness), Duration = PhaseFade });
        }

        // Reconciles the chip row with the new queue: chips dropped off the cap
        // fade out, the freshly-appended chip fades and slides in, the rest are
        // left untouched.
        private void UpdateChips(IReadOnlyList<ActivityLabel> messages)
        {
            var offset = FindShiftOffset(messages);
            if (offset < 0)
            {
                // Unrelated set (manual indicator, or a post-idle restart):
                // rebuild without per-chip animation.
                labelPanel.Children.Clear();
                chipModels.Clear();
                chipElements.Clear();
                foreach (var message in messages)
                {
                    var rebuilt = CreateChip(message);
                    chipModels.Add(message);
                    chipElements.Add(rebuilt);
                    labelPanel.Children.Add(rebuilt);
                }

                RestyleChips();
                return;
            }

            // Fade out the leading chips that dropped off the cap.
            for (var i = 0; i < offset; i++)
            {
                FadeChipOut(chipElements[i]);
            }

            chipModels.RemoveRange(0, offset);
            chipElements.RemoveRange(0, offset);

            // Append and animate in the freshly-queued chips.
            for (var i = chipModels.Count; i < messages.Count; i++)
            {
                var chip = CreateChip(messages[i]);
                chipModels.Add(messages[i]);
                chipElements.Add(chip);
                labelPanel.Children.Add(chip);
                AnimateChipIn(chip);
            }

            RestyleChips();
        }

        // Smallest offset where the remaining current chips are a prefix of the
        // new queue (a queue shift drops 0..n from the front and appends).
        // -1 when the new queue is unrelated to what is shown.
        private int FindShiftOffset(IReadOnlyList<ActivityLabel> messages)
        {
            for (var offset = 0; offset <= chipModels.Count; offset++)
            {
                var kept = chipModels.Count - offset;
                if (kept > messages.Count)
                {
                    continue;
                }

                var match = true;
                for (var i = 0; i < kept; i++)
                {
                    if (!chipModels[offset + i].Equals(messages[i]))
                    {
                        match = false;
                        break;
                    }
                }

                if (match)
                {
                    return offset;
                }
            }

            return -1;
        }

        private static Border CreateChip(ActivityLabel message)
        {
            return new Border
            {
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(7, 3, 7, 3),
                Margin = new Thickness(0, 0, 4, 0),
                RenderTransform = new TranslateTransform(),
                Child = new TextBlock
                {
                    Text = message.Text,
                    Foreground = System.Windows.Media.Brushes.White,
                    FontSize = 12
                }
            };
        }

        // Fades and slides a freshly-appended chip in from the right.
        private static void AnimateChipIn(Border chip)
        {
            var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
            chip.Opacity = 0;
            var slide = (TranslateTransform)chip.RenderTransform;
            slide.X = ChipSlide;

            chip.BeginAnimation(OpacityProperty, new DoubleAnimation { From = 0, To = 1, Duration = ChipMotion, EasingFunction = ease });
            slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation { From = ChipSlide, To = 0, Duration = ChipMotion, EasingFunction = ease });
        }

        // Fades a dropped chip out, then removes it from the panel.
        private void FadeChipOut(Border chip)
        {
            var fade = new DoubleAnimation
            {
                To = 0,
                Duration = new Duration(TimeSpan.FromMilliseconds(160)),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };
            fade.Completed += (_, _) => labelPanel.Children.Remove(chip);
            chip.BeginAnimation(OpacityProperty, fade);
        }

        // The newest chip stands out (brighter, semi-bold); older chips recede.
        private void RestyleChips()
        {
            for (var i = 0; i < chipElements.Count; i++)
            {
                var newest = i == chipElements.Count - 1;
                var message = chipModels[i];
                var alpha = (byte)(newest ? 220 : 145);
                chipElements[i].Background = message.Elevated
                    ? new SolidColorBrush(System.Windows.Media.Color.FromArgb(alpha, 150, 70, 0))
                    : new SolidColorBrush(System.Windows.Media.Color.FromArgb(alpha, 0, 0, 0));
                ((TextBlock)chipElements[i].Child).FontWeight = newest ? FontWeights.SemiBold : FontWeights.Normal;
            }
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
